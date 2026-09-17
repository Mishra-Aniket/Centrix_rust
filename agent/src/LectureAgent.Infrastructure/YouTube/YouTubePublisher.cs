namespace LectureAgent.Infrastructure.YouTube;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// OAuth installed-app YouTube publisher. Videos are always created as unlisted;
/// an unpublish action changes the privacy setting to private instead of deleting data.
/// </summary>
public sealed class YouTubePublisher : IYouTubePublisher
{
    private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeUpload };
    private readonly IConfiguration _configuration;
    private readonly ILogger<YouTubePublisher> _logger;
    private readonly ICredentialProtector _protector;
    private readonly IGoogleDriveUploader? _driveUploader;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private YouTubeService? _service;

    public YouTubePublisher(
        IConfiguration configuration,
        ILogger<YouTubePublisher> logger,
        ICredentialProtector? protector = null,
        IGoogleDriveUploader? driveUploader = null)
    {
        _configuration = configuration;
        _logger = logger;
        _protector = protector ?? new CredentialProtector();
        _driveUploader = driveUploader;
    }

    public async Task<YouTubePublishResult> PublishAsync(LectureSession lecture, CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(cancellationToken);
        var metadata = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = BuildTitle(lecture),
                Description = BuildDescription(lecture),
                CategoryId = _configuration["YouTube:CategoryId"] ?? "27"
            },
            Status = new VideoStatus { PrivacyStatus = "unlisted" }
        };

        Stream videoStream;
        Func<ValueTask>? streamDisposer = null;
        string mimeType = "video/mp4";

        if (!string.IsNullOrWhiteSpace(lecture.DriveVideoFileId) && _driveUploader != null)
        {
            _logger.LogInformation("Streaming lecture {LectureId} directly from Google Drive (ID: {DriveFileId}) into YouTube",
                lecture.LectureSessionId, lecture.DriveVideoFileId);
            var driveStream = await _driveUploader.OpenDownloadStreamAsync(lecture.DriveVideoFileId, cancellationToken);
            videoStream = driveStream;
            streamDisposer = async () => await driveStream.DisposeAsync();
            if (!string.IsNullOrWhiteSpace(lecture.VideoFileLocalPath))
                mimeType = GetMimeType(lecture.VideoFileLocalPath);
        }
        else if (!string.IsNullOrWhiteSpace(lecture.VideoFileLocalPath) && File.Exists(lecture.VideoFileLocalPath))
        {
            _logger.LogInformation("Uploading lecture {LectureId} from local file {LocalPath} into YouTube",
                lecture.LectureSessionId, lecture.VideoFileLocalPath);
            var localStream = new FileStream(lecture.VideoFileLocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            videoStream = localStream;
            streamDisposer = async () => await localStream.DisposeAsync();
            mimeType = GetMimeType(lecture.VideoFileLocalPath);
        }
        else
        {
            throw new FileNotFoundException(
                "Neither a Google Drive video ID nor a valid local video file was found for YouTube publishing.",
                lecture.VideoFileLocalPath);
        }

        try
        {
            var request = service.Videos.Insert(metadata, new[] { "snippet", "status" }, videoStream, mimeType);
            request.ChunkSize = ResumableUpload.MinimumChunkSize * 4;
            var progress = await request.UploadAsync(cancellationToken);
            if (progress.Status != UploadStatus.Completed || string.IsNullOrWhiteSpace(request.ResponseBody?.Id))
                throw new IOException($"YouTube upload failed with status {progress.Status}: {progress.Exception?.Message}");

            var videoId = request.ResponseBody.Id;
            _logger.LogInformation("Published lecture {LectureId} to YouTube as unlisted video {VideoId}", lecture.LectureSessionId, videoId);
            return new YouTubePublishResult
            {
                VideoId = videoId,
                ThumbnailUrl = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg"
            };
        }
        finally
        {
            if (streamDisposer != null)
            {
                await streamDisposer();
            }
        }
    }

    public async Task UnpublishAsync(string youTubeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(youTubeId))
            throw new ArgumentException("A YouTube video ID is required.", nameof(youTubeId));

        var service = await GetServiceAsync(cancellationToken);
        var request = service.Videos.Update(new Video
        {
            Id = youTubeId,
            Status = new VideoStatus { PrivacyStatus = "private" }
        }, new[] { "status" });
        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Made YouTube video {VideoId} private", youTubeId);
    }

    internal static string BuildTitle(LectureSession lecture)
    {
        var topic = string.Join(" — ", new[] { lecture.BatchId, lecture.SubjectId }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(topic)
            ? $"Lecture {lecture.DetectedStartTime:yyyy-MM-dd}"
            : $"{topic} | {lecture.DetectedStartTime:dd MMM yyyy}";
    }

    private static string BuildDescription(LectureSession lecture) =>
        $"Centrix lecture recording\nCenter: {lecture.CenterId}\nRoom: {lecture.RoomId}\nRecorded: {lecture.DetectedStartTime:u}";

    private async Task<YouTubeService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (_service != null)
            return _service;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_service != null)
                return _service;

            var credentialsPath = ResolvePath(_configuration["YouTube:CredentialsPath"] ?? _configuration["GoogleDrive:CredentialsPath"]);
            if (string.IsNullOrWhiteSpace(credentialsPath) || (!File.Exists(credentialsPath) && !File.Exists(credentialsPath + ".protected")))
            {
                throw new InvalidOperationException(
                    "YouTube OAuth credentials are missing. Configure YouTube:CredentialsPath with an installed-app Google OAuth client JSON file.");
            }

            GoogleClientSecrets secrets;
            await using (var stream = _protector.OpenRead(credentialsPath))
                secrets = GoogleClientSecrets.FromStream(stream);

            var tokenPath = ResolvePath(_configuration["YouTube:TokenPath"] ?? "data/youtube-token");
            var tokenFile = Path.Combine(tokenPath, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-lecture-agent-youtube");
            if (!File.Exists(tokenFile))
            {
                throw new InvalidOperationException(
                    "YouTube account not connected yet. Please connect your YouTube channel first by clicking 'Connect YouTube' in the YouTube Publishing section.");
            }

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets.Secrets,
                Scopes,
                "lecture-agent-youtube",
                cancellationToken,
                new FileDataStore(tokenPath, true));

            _service = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Centrix Lecture Publisher"
            });
            return _service;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static string ResolvePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".avi" => "video/x-msvideo",
        _ => "video/x-matroska"
    };
}
