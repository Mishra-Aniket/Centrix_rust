using System.Collections.Concurrent;
using System.Text;
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
using LectureAgent.Infrastructure.Security;
using LocalFile = System.IO.File;

namespace LectureAgent.Infrastructure.Cloud;

public sealed class GoogleDriveUploader : IGoogleDriveUploader
{
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly UploadProgressStore _progressStore;
    private readonly ILogger<GoogleDriveUploader> _logger;
    private readonly ICredentialProtector _protector;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _folderIdCache = new(StringComparer.OrdinalIgnoreCase);
    private DriveService? _driveService;

    public GoogleDriveUploader(
        IConfiguration configuration,
        IHostEnvironment environment,
        UploadProgressStore progressStore,
        ILogger<GoogleDriveUploader> logger,
        ICredentialProtector? protector = null)
    {
        _configuration = configuration;
        _environment = environment;
        _progressStore = progressStore;
        _logger = logger;
        _protector = protector ?? new CredentialProtector();
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
        var folderId = await ResolveOrCreateFolderPathAsync(service, entry.DriveFolderPath);
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
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var service = await GetDriveServiceAsync();
        await ResolveOrCreateFolderPathAsync(service, folderPath);
    }

    public async Task<List<string>> ListFoldersAsync(string? query = null, int limit = 200, string? underPath = null, CancellationToken cancellationToken = default)
    {
        var service = await GetDriveServiceAsync();
        var qParts = new List<string>
        {
            "mimeType = 'application/vnd.google-apps.folder'",
            "trashed = false"
        };

        if (!string.IsNullOrWhiteSpace(underPath))
        {
            try
            {
                var parentId = await ResolveOrCreateFolderPathAsync(service, underPath);
                qParts.Add($"'{parentId}' in parents");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not resolve underPath '{UnderPath}': {Message}", underPath, ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var escaped = query.Replace("'", "\\'");
            qParts.Add($"name contains '{escaped}'");
        }

        var listRequest = service.Files.List();
        listRequest.Q = string.Join(" and ", qParts);
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;
        listRequest.Spaces = "drive";
        listRequest.Fields = "files(id, name)";
        listRequest.PageSize = Math.Clamp(limit, 1, 1000);

        var result = await listRequest.ExecuteAsync(cancellationToken);
        return result.Files?
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList() ?? new List<string>();
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
            if (string.IsNullOrWhiteSpace(credentialsPath))
            {
                var sharedCreds = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "LectureAgent", "config", "google_credentials.json");
                if (LocalFile.Exists(sharedCreds) || LocalFile.Exists(sharedCreds + ".protected"))
                {
                    credentialsPath = sharedCreds;
                }
                else
                {
                    credentialsPath = ResolvePath("config/google_credentials.json");
                }
            }

            if (string.IsNullOrWhiteSpace(credentialsPath)
                || (!LocalFile.Exists(credentialsPath) && !LocalFile.Exists(credentialsPath + ".protected")))
            {
                throw new InvalidOperationException(
                    $"Google Drive credentials file not found: {credentialsPath}. "
                    + "Please select a valid OAuth credentials file in Centrix Settings.");
            }

            var tokenPath = ResolvePath(_configuration["GoogleDrive:TokenPath"] ?? "data/google-drive-token");
            if (string.IsNullOrWhiteSpace(tokenPath))
            {
                tokenPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "LectureAgent", "data", "google-drive-token");
            }

            GoogleClientSecrets secrets;
            using (var stream = _protector.OpenRead(credentialsPath))
            {
                secrets = GoogleClientSecrets.FromStream(stream);
            }

            UserCredential credential;
            try
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets.Secrets,
                    Scopes,
                    "lecture-agent",
                    CancellationToken.None,
                    new FileDataStore(tokenPath, true));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Google Drive authorization failed or has not been completed yet. "
                    + "Open the Centrix desktop app, go to Settings, and click 'Sign in to Google now…'.", ex);
            }

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
    /// Resolves or automatically creates a folder hierarchy on Google Drive.
    /// Handles arbitrary folder names, custom root folder names, casing differences,
    /// special characters, and multi-segment paths (e.g. "Center/Room/Date/Batch" or "BatchName").
    /// If folders do not exist on Drive, it automatically creates them under the appropriate parent.
    /// </summary>
    private async Task<string> ResolveOrCreateFolderPathAsync(DriveService service, string? folderPath)
    {
        // 1. Resolve Root Folder (if configured)
        var rootFolderName = _configuration["GoogleDrive:RootFolderPath"];
        if (string.IsNullOrWhiteSpace(rootFolderName))
        {
            rootFolderName = _configuration["GoogleDriveRootFolder"];
        }

        string currentParentId = "root";

        if (!string.IsNullOrWhiteSpace(rootFolderName) && !rootFolderName.Trim().Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            currentParentId = await GetOrCreateFolderSegmentAsync(service, rootFolderName.Trim(), "root");
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return currentParentId;
        }

        // 2. Parse path segments (support both '/' and '\')
        var segments = folderPath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (segments.Count == 0)
        {
            return currentParentId;
        }

        // If folder path already starts with the root folder name, skip that first segment to avoid duplicate nesting
        if (!string.IsNullOrWhiteSpace(rootFolderName) &&
            segments[0].Equals(rootFolderName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count == 0)
        {
            return currentParentId;
        }

        // 3. For single-segment paths (e.g. just a batch name "27-AJ452NA 2026"):
        // Check if an existing folder with this name exists under the current parent, or globally across the account!
        if (segments.Count == 1)
        {
            var singleName = segments[0];
            var existingUnderParent = await FindExistingFolderUnderParentAsync(service, singleName, currentParentId);
            if (!string.IsNullOrWhiteSpace(existingUnderParent))
            {
                return existingUnderParent;
            }

            var existingGlobal = await FindExistingFolderGlobalAsync(service, singleName);
            if (!string.IsNullOrWhiteSpace(existingGlobal))
            {
                return existingGlobal;
            }
        }

        // 4. Traverse/Create each segment in the hierarchy
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            bool isFirst = (i == 0);
            currentParentId = await GetOrCreateFolderSegmentAsync(service, segment, currentParentId, isFirst);
        }

        return currentParentId;
    }

    private async Task<string> GetOrCreateFolderSegmentAsync(DriveService service, string segmentName, string parentId, bool isFirstSegment = false)
    {
        var cacheKey = $"{parentId}:{segmentName}";
        if (_folderIdCache.TryGetValue(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        // 1. Try finding existing folder under parentId
        var existingId = await FindExistingFolderUnderParentAsync(service, segmentName, parentId);
        if (!string.IsNullOrWhiteSpace(existingId))
        {
            _folderIdCache[cacheKey] = existingId;
            return existingId;
        }

        // 2. If parent is "root" OR this is the first segment (batch folder like 27-AJ451NA 2026),
        // search globally on Google Drive so nested folders under Shift/Program are found automatically!
        if (parentId.Equals("root", StringComparison.OrdinalIgnoreCase) || isFirstSegment)
        {
            var globalId = await FindExistingFolderGlobalAsync(service, segmentName);
            if (!string.IsNullOrWhiteSpace(globalId))
            {
                _folderIdCache[cacheKey] = globalId;
                return globalId;
            }
        }

        // 3. Auto-create folder on Google Drive
        var autoCreate = _configuration.GetValue("GoogleDrive:AutoCreateFolders", true);
        if (!autoCreate)
        {
            throw new InvalidOperationException(
                $"Drive folder '{segmentName}' not found and AutoCreateFolders is disabled.");
        }

        var folderMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = segmentName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = new[] { parentId }
        };

        var createRequest = service.Files.Create(folderMetadata);
        createRequest.SupportsAllDrives = true;
        createRequest.Fields = "id, name, parents";

        var createdFolder = await createRequest.ExecuteAsync();
        if (createdFolder == null || string.IsNullOrWhiteSpace(createdFolder.Id))
        {
            throw new IOException($"Failed to create Drive folder '{segmentName}' under parent '{parentId}'.");
        }

        _logger.LogInformation("Auto-created Google Drive folder '{FolderName}' (ID: {FolderId}) under parent {ParentId}",
            segmentName, createdFolder.Id, parentId);

        _folderIdCache[cacheKey] = createdFolder.Id;
        return createdFolder.Id;
    }

    private async Task<string?> FindExistingFolderUnderParentAsync(DriveService service, string segmentName, string parentId)
    {
        try
        {
            var escapedName = EscapeDriveQuery(segmentName);
            var parentFilter = parentId.Equals("root", StringComparison.OrdinalIgnoreCase)
                ? "'root' in parents"
                : $"'{parentId}' in parents";

            var listRequest = service.Files.List();
            listRequest.Q = $"name = '{escapedName}' and {parentFilter} and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            listRequest.SupportsAllDrives = true;
            listRequest.IncludeItemsFromAllDrives = true;
            listRequest.Spaces = "drive";
            listRequest.Fields = "files(id, name)";
            listRequest.PageSize = 5;

            var result = await listRequest.ExecuteAsync();
            var exactMatch = result.Files?.FirstOrDefault();
            if (exactMatch != null && !string.IsNullOrWhiteSpace(exactMatch.Id))
            {
                return exactMatch.Id;
            }

            // Flexible fallback: list subfolders under parent and compare case-insensitively / trimmed
            var flexibleListRequest = service.Files.List();
            flexibleListRequest.Q = $"{parentFilter} and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            flexibleListRequest.SupportsAllDrives = true;
            flexibleListRequest.IncludeItemsFromAllDrives = true;
            flexibleListRequest.Spaces = "drive";
            flexibleListRequest.Fields = "files(id, name)";
            flexibleListRequest.PageSize = 100;

            var allChildren = await flexibleListRequest.ExecuteAsync();
            if (allChildren.Files != null)
            {
                var normalizedTarget = NormalizeFolderName(segmentName);
                var match = allChildren.Files.FirstOrDefault(f =>
                    string.Equals(f.Name?.Trim(), segmentName.Trim(), StringComparison.OrdinalIgnoreCase)
                    || NormalizeFolderName(f.Name) == normalizedTarget
                    || AreSubjectNamesMatching(f.Name ?? "", segmentName));

                if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                {
                    _logger.LogInformation("Matched Drive folder '{FolderName}' (ID: {FolderId}) via flexible name matching for '{TargetName}'",
                        match.Name, match.Id, segmentName);
                    return match.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error searching for Drive folder '{FolderName}' under '{ParentId}': {Message}",
                segmentName, parentId, ex.Message);
        }

        return null;
    }

    private async Task<string?> FindExistingFolderGlobalAsync(DriveService service, string segmentName)
    {
        try
        {
            var escapedName = EscapeDriveQuery(segmentName);
            var search = service.Files.List();
            search.Q = $"name = '{escapedName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            search.SupportsAllDrives = true;
            search.IncludeItemsFromAllDrives = true;
            search.Spaces = "drive";
            search.Fields = "files(id, name, parents)";
            search.PageSize = 5;

            var match = await search.ExecuteAsync();
            var folder = match.Files?.FirstOrDefault();
            if (folder != null && !string.IsNullOrWhiteSpace(folder.Id))
            {
                _logger.LogInformation("Found existing Drive folder '{FolderName}' globally (ID: {FolderId})",
                    folder.Name, folder.Id);
                return folder.Id;
            }

            // Flexible global search if contains special characters or spaces
            var searchContains = service.Files.List();
            var keyword = segmentName.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(k => k.Length >= 4) ?? segmentName;
            searchContains.Q = $"name contains '{EscapeDriveQuery(keyword)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            searchContains.SupportsAllDrives = true;
            searchContains.IncludeItemsFromAllDrives = true;
            searchContains.Spaces = "drive";
            searchContains.Fields = "files(id, name, parents)";
            searchContains.PageSize = 30;

            var containsResult = await searchContains.ExecuteAsync();
            if (containsResult.Files != null)
            {
                var targetNormalized = NormalizeFolderName(segmentName);
                var flexibleMatch = containsResult.Files.FirstOrDefault(f =>
                {
                    if (string.Equals(f.Name?.Trim(), segmentName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;

                    var fileNorm = NormalizeFolderName(f.Name);
                    if (fileNorm == targetNormalized)
                        return true;

                    // Flexible match when year or prefix differs (e.g. 27-AJ451NA vs 27-AJ451NA 2026)
                    if (!string.IsNullOrEmpty(targetNormalized) && !string.IsNullOrEmpty(fileNorm))
                    {
                        if (fileNorm.Contains(targetNormalized) || targetNormalized.Contains(fileNorm))
                            return true;
                    }

                    return false;
                });

                if (flexibleMatch != null && !string.IsNullOrWhiteSpace(flexibleMatch.Id))
                {
                    _logger.LogInformation("Found existing Drive folder '{FolderName}' globally via flexible matching for '{TargetName}' (ID: {FolderId})",
                        flexibleMatch.Name, segmentName, flexibleMatch.Id);
                    return flexibleMatch.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Global search for Drive folder '{FolderName}' encountered error: {Message}",
                segmentName, ex.Message);
        }

        return null;
    }

    private static string EscapeDriveQuery(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string NormalizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    private static bool AreSubjectNamesMatching(string name1, string name2)
    {
        var norm1 = NormalizeFolderName(name1);
        var norm2 = NormalizeFolderName(name2);
        if (norm1 == norm2) return true;
        if (string.IsNullOrEmpty(norm1) || string.IsNullOrEmpty(norm2)) return false;

        // Subject alias pairs (PW Vidyapeeth / JEE / NEET standard subjects)
        if ((norm1.StartsWith("math") || norm1.StartsWith("mathem")) && (norm2.StartsWith("math") || norm2.StartsWith("mathem"))) return true;
        if (norm1.StartsWith("chem") && norm2.StartsWith("chem")) return true;
        if (norm1.StartsWith("phy") && norm2.StartsWith("phy")) return true;
        if (norm1.StartsWith("bio") && norm2.StartsWith("bio")) return true;
        if (norm1.StartsWith("bot") && norm2.StartsWith("bot")) return true;
        if (norm1.StartsWith("zoo") && norm2.StartsWith("zoo")) return true;

        if (norm1.Length >= 3 && norm2.Length >= 3)
        {
            if (norm1.StartsWith(norm2) || norm2.StartsWith(norm1))
                return true;
        }

        return false;
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
