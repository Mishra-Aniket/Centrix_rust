using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LocalFile = System.IO.File;

namespace LectureAgent.Infrastructure.Cloud;

public sealed class GoogleDriveUploader : IGoogleDriveUploader
{
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly UploadProgressStore _progressStore;
    private readonly ILogger<GoogleDriveUploader> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private DriveService? _driveService;

    public GoogleDriveUploader(
        IConfiguration configuration,
        IHostEnvironment environment,
        UploadProgressStore progressStore,
        ILogger<GoogleDriveUploader> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _progressStore = progressStore;
        _logger = logger;
    }

    public async Task<string> AuthorizeAsync()
    {
        await GetDriveServiceAsync();
        return "authorized";
    }

    public async Task<string> UploadFileAsync(UploadQueueEntry entry, CancellationToken cancellationToken = default)
    {
        if (!LocalFile.Exists(entry.LocalFilePath))
            throw new FileNotFoundException("Upload file was not found", entry.LocalFilePath);

        var service = await GetDriveServiceAsync();
        var folderId = await FindExistingFolderPathAsync(service, entry.DriveFolderPath);
        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = entry.DriveFileName ?? Path.GetFileName(entry.LocalFilePath),
            Parents = new[] { folderId }
        };

        await using var stream = new FileStream(entry.LocalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var upload = service.Files.Create(metadata, stream, GetMimeType(entry));
        upload.Fields = "id, name, size";
        upload.SupportsAllDrives = true;
        upload.ChunkSize = ResumableUpload.MinimumChunkSize * 4;
        upload.ProgressChanged += progress =>
            _progressStore.Set(entry.QueueEntryId, progress.BytesSent);

        var uploadProgress = await upload.UploadAsync(cancellationToken);
        if (uploadProgress.Status != UploadStatus.Completed || string.IsNullOrWhiteSpace(upload.ResponseBody?.Id))
            throw new IOException($"Google Drive upload failed with status {uploadProgress.Status}: {uploadProgress.Exception?.Message}");

        return upload.ResponseBody.Id;
    }

    public async Task<bool> VerifyUploadAsync(string fileId, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return false;

        try
        {
            var service = await GetDriveServiceAsync();
            var getRequest = service.Files.Get(fileId);
            getRequest.Fields = "id, name, size, md5Checksum, trashed";
            getRequest.SupportsAllDrives = true;
            var file = await getRequest.ExecuteAsync();

            if (file == null || string.IsNullOrWhiteSpace(file.Id) || file.Trashed == true)
                return false;

            // Ensure uploaded file has non-zero size
            if (file.Size.GetValueOrDefault() <= 0)
            {
                _logger.LogWarning("Google Drive file {FileId} has 0 bytes reported", fileId);
                return false;
            }

            // If MD5 hash was provided, verify it directly against Drive's md5Checksum
            if (!string.IsNullOrWhiteSpace(expectedHash) && expectedHash.StartsWith("md5:", StringComparison.OrdinalIgnoreCase))
            {
                var cleanExpected = expectedHash.Substring(4).Trim();
                if (!string.IsNullOrWhiteSpace(file.Md5Checksum) &&
                    !string.Equals(file.Md5Checksum, cleanExpected, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("MD5 mismatch for {FileId}: Drive={DriveMd5}, Expected={Expected}",
                        fileId, file.Md5Checksum, cleanExpected);
                    return false;
                }
            }

            return true;
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogWarning("Google Drive verification failed for {FileId}: {Message}", fileId, ex.Message);
            return false;
        }
    }

    public async Task CreateFolderStructureAsync(string folderPath)
    {
        // Intentional no-op: the agent only uploads into EXISTING batch folders.
        // It never creates folders on Google Drive.
        await Task.CompletedTask;
    }

    private async Task<DriveService> GetDriveServiceAsync()
    {
        if (_driveService != null)
            return _driveService;

        await _initializationLock.WaitAsync();
        try
        {
            if (_driveService != null)
                return _driveService;

            var credentialsPath = _configuration["GoogleDrive:CredentialsPath"];
            credentialsPath = ResolvePath(credentialsPath);
            if (string.IsNullOrWhiteSpace(credentialsPath) || !LocalFile.Exists(credentialsPath))
                throw new InvalidOperationException($"Google Drive credentials file not found: {credentialsPath}");

            var tokenPath = ResolvePath(_configuration["GoogleDrive:TokenPath"] ?? "data/google-drive-token");
            var secrets = GoogleClientSecrets.FromFile(credentialsPath).Secrets;
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "lecture-agent",
                CancellationToken.None,
                new FileDataStore(tokenPath, true));

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "LectureAgent"
            });
            return _driveService;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Finds an EXISTING folder by name (the last segment of the path, e.g. the batch
    /// folder "27-AJ452NA 2026") across the account and shared drives. Folders are
    /// pre-created by the center, so this never creates anything: if the folder is
    /// missing it throws with a clear message instead of creating a duplicate tree.
    /// </summary>
    private async Task<string> FindExistingFolderPathAsync(DriveService service, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new InvalidOperationException(
                "No Drive folder specified for this lecture (batch unknown). Fix the assignment on the dashboard first.");

        var segments = folderPath.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targetFolderName = segments.LastOrDefault()
            ?? throw new InvalidOperationException($"Invalid Drive folder path: '{folderPath}'");

        var escapedName = targetFolderName.Replace("'", "\\'");
        var search = service.Files.List();
        search.Q = $"name = '{escapedName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        search.SupportsAllDrives = true;
        search.IncludeItemsFromAllDrives = true;
        search.Spaces = "drive";
        search.Fields = "files(id, name, parents)";
        search.PageSize = 5;

        var match = await search.ExecuteAsync();
        var folder = match.Files?.FirstOrDefault();

        if (folder == null)
        {
            throw new InvalidOperationException(
                $"Drive folder '{targetFolderName}' not found. Batch folders are pre-created by the center - " +
                "create this batch folder on Google Drive (or fix the lecture's batch on the dashboard), then retry the upload. " +
                "The agent never creates folders.");
        }

        if (match.Files!.Count > 1)
        {
            _logger.LogWarning(
                "Multiple Drive folders named '{FolderName}' found; uploading into the first one (ID: {FolderId})",
                targetFolderName, folder.Id);
        }

        _logger.LogInformation("Uploading into existing Drive folder '{FolderName}' (ID: {FolderId})",
            folder.Name, folder.Id);
        return folder.Id;
    }

    private static string GetMimeType(UploadQueueEntry entry) =>
        entry.FileType.Equals("PDF", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "video/*";

    private string? ResolvePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? path
            : Path.IsPathRooted(path)
                ? path
                : Path.Combine(_environment.ContentRootPath, path);
}
