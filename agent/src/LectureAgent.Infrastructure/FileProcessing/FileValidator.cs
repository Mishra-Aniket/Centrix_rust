namespace LectureAgent.Infrastructure.FileProcessing;

using LectureAgent.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

/// <summary>
/// Validates files and extracts metadata.
/// </summary>
public class FileValidator : IFileValidator
{
    private readonly ILogger<FileValidator> _logger;
    private readonly IConfiguration? _configuration;
    private readonly string[] _allowedVideoExtensions = { ".mkv", ".mp4", ".mov", ".avi", ".webm" };
    private readonly string[] _allowedPdfExtensions = { ".pdf" };

    public FileValidator(ILogger<FileValidator> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<bool> IsFileStableAsync(string filePath, int stabilityCheckMs = 1000)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            var fileInfo = new FileInfo(filePath);
            var initialSize = fileInfo.Length;

            if (initialSize <= 0)
                return false;

            await Task.Delay(stabilityCheckMs);

            fileInfo.Refresh();
            var finalSize = fileInfo.Length;

            if (initialSize != finalSize || finalSize <= 0)
                return false;

            // Check if active write lock is released
            try
            {
                using var fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return fs.Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch
            {
                try
                {
                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return fs.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Stability check failed: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Valid, string? Error)> ValidateFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return (false, "File does not exist");

            var fileInfo = new FileInfo(filePath);
            var ext = fileInfo.Extension.ToLower();

            // Check extension
            var isVideo = _allowedVideoExtensions.Contains(ext);
            var isPdf = _allowedPdfExtensions.Contains(ext);

            if (!isVideo && !isPdf)
                return (false, $"Unsupported file type: {ext}");

            // Check file size (minimum 1MB for video, 50KB for PDF)
            if (isVideo && fileInfo.Length < 1024 * 1024)
                return (false, "Video file too small");

            if (isPdf && fileInfo.Length < 50 * 1024)
                return (false, "PDF file too small");

            // Check if file is readable
            try
            {
                using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length == 0)
                    return (false, "File is empty");
            }
            catch (Exception ex)
            {
                return (false, $"Cannot read file: {ex.Message}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Validation error: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public async Task<string> CalculateHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        
        var hash = await Task.Run(() => sha256.ComputeHash(fileStream));
        return $"sha256:{BitConverter.ToString(hash).Replace("-", "").ToLower()}";
    }

    public async Task<VideoMetadata?> ExtractVideoMetadataAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                return new VideoMetadata { Codec = "pdf", Source = "extension" };

            var binary = ResolveFFprobePath(_configuration?["FileProcessing:FFprobePath"]);

            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"-v quiet -print_format json -show_streams -show_format \"{filePath.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return new VideoMetadata { Codec = "unknown", Resolution = "unknown", DurationSeconds = 0, Source = "fallback" };

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return new VideoMetadata { Codec = "unknown", Resolution = "unknown", DurationSeconds = 0, Source = "fallback" };

            var metadata = ParseFFprobeOutput(output);
            return metadata ?? new VideoMetadata { Codec = "unknown", Resolution = "unknown", DurationSeconds = 0, Source = "fallback" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Metadata extraction unavailable: {ex.Message}");
            return new VideoMetadata { Codec = "unknown", Resolution = "unknown", DurationSeconds = 0, Source = "fallback" };
        }
    }

    public static string ResolveFFprobePath(
        string? configured,
        string? baseDirectory = null,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        baseDirectory ??= AppContext.BaseDirectory;

        var defaultName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

        if (string.IsNullOrWhiteSpace(configured) || string.Equals(configured, "ffprobe", StringComparison.OrdinalIgnoreCase))
        {
            var bundled = Path.Combine(baseDirectory, defaultName);
            if (fileExists(bundled))
                return bundled;

            return defaultName;
        }

        if (Path.IsPathRooted(configured))
            return configured;

        var candidate = Path.Combine(baseDirectory, configured);
        if (fileExists(candidate))
            return candidate;

        return configured;
    }

    public static VideoMetadata? ParseFFprobeOutput(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                return null;

            JsonElement videoStream = default;
            var hasVideo = false;
            foreach (var s in streams.EnumerateArray())
            {
                if (s.TryGetProperty("codec_type", out var type) && string.Equals(type.GetString(), "video", StringComparison.OrdinalIgnoreCase))
                {
                    videoStream = s;
                    hasVideo = true;
                    break;
                }
            }

            if (!hasVideo)
                return null;

            double durationSeconds = 0;
            var durationFound = false;

            if (document.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var formatDuration))
            {
                if (formatDuration.ValueKind == JsonValueKind.Number && formatDuration.TryGetDouble(out var num))
                {
                    durationSeconds = num;
                    durationFound = true;
                }
                else if (formatDuration.ValueKind == JsonValueKind.String && double.TryParse(formatDuration.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var strNum))
                {
                    durationSeconds = strNum;
                    durationFound = true;
                }
            }

            if (!durationFound && videoStream.TryGetProperty("duration", out var streamDuration))
            {
                if (streamDuration.ValueKind == JsonValueKind.Number && streamDuration.TryGetDouble(out var num))
                {
                    durationSeconds = num;
                    durationFound = true;
                }
                else if (streamDuration.ValueKind == JsonValueKind.String && double.TryParse(streamDuration.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var strNum))
                {
                    durationSeconds = strNum;
                    durationFound = true;
                }
            }

            var roundedDuration = (int)Math.Round(durationSeconds, MidpointRounding.AwayFromZero);
            var codec = videoStream.TryGetProperty("codec_name", out var c) ? c.GetString() : "unknown";
            var width = videoStream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
            var height = videoStream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
            var resolution = width > 0 && height > 0 ? $"{width}x{height}" : null;
            var frameRate = ParseFrameRate(videoStream);

            return new VideoMetadata
            {
                DurationSeconds = roundedDuration,
                Codec = string.IsNullOrWhiteSpace(codec) ? "unknown" : codec,
                Resolution = resolution,
                FrameRate = frameRate,
                Source = "ffprobe"
            };
        }
        catch
        {
            return null;
        }
    }

    private static int? ParseFrameRate(JsonElement stream)
    {
        if (!stream.TryGetProperty("r_frame_rate", out var frameRateProperty))
            return null;

        var parts = frameRateProperty.GetString()?.Split('/');
        if (parts?.Length != 2
            || !double.TryParse(parts[0], out var numerator)
            || !double.TryParse(parts[1], out var denominator)
            || denominator == 0)
            return null;

        return (int)Math.Round(numerator / denominator);
    }
}
