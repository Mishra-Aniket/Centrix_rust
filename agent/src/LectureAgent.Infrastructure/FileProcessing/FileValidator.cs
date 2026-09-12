namespace LectureAgent.Infrastructure.FileProcessing;

using LectureAgent.Domain.Services;
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
    private readonly string[] _allowedVideoExtensions = { ".mkv", ".mp4", ".mov", ".avi", ".webm" };
    private readonly string[] _allowedPdfExtensions = { ".pdf" };

    public FileValidator(ILogger<FileValidator> logger)
    {
        _logger = logger;
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
                return new VideoMetadata { Codec = "pdf" };

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v quiet -print_format json -show_streams \"{filePath.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return new VideoMetadata { Codec = "unknown", Resolution = "unknown" };

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return new VideoMetadata { Codec = "unknown", Resolution = "unknown" };

            using var document = JsonDocument.Parse(output);
            var stream = document.RootElement.TryGetProperty("streams", out var streams)
                ? streams.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("codec_type", out var type) && type.GetString() == "video")
                : default;

            if (stream.ValueKind == JsonValueKind.Undefined)
                return new VideoMetadata { Codec = "unknown", Resolution = "unknown" };

            var duration = stream.TryGetProperty("duration", out var durationProperty)
                && double.TryParse(durationProperty.GetString(), out var seconds)
                ? (int)Math.Round(seconds)
                : 0;
            var width = stream.TryGetProperty("width", out var widthProperty) ? widthProperty.GetInt32() : 0;
            var height = stream.TryGetProperty("height", out var heightProperty) ? heightProperty.GetInt32() : 0;
            var frameRate = ParseFrameRate(stream);

            return new VideoMetadata
            {
                DurationSeconds = duration,
                Codec = stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() : "unknown",
                Resolution = width > 0 && height > 0 ? $"{width}x{height}" : "unknown",
                FrameRate = frameRate
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Metadata extraction unavailable: {ex.Message}");
            return new VideoMetadata { Codec = "unknown", Resolution = "unknown" };
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
