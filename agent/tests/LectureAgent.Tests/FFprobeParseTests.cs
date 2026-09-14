using LectureAgent.Infrastructure.FileProcessing;
using Xunit;

namespace LectureAgent.Tests;

/// <summary>
/// ffprobe JSON parsing without requiring ffprobe on the test machine.
/// The parser must prefer the container (format) duration: MKV/WebM recordings from
/// the lecture detector usually carry duration there, not on the video stream.
/// </summary>
public class FFprobeParseTests
{
    [Fact]
    public void ParseFFprobeOutput_FormatDurationWins()
    {
        const string json = """
            {
                "streams": [
                    { "codec_type": "audio", "codec_name": "aac", "duration": "5400.0" },
                    { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080,
                      "r_frame_rate": "25/1", "duration": "5400.523000" }
                ],
                "format": { "duration": "5401.024000" }
            }
            """;

        var metadata = FileValidator.ParseFFprobeOutput(json);

        Assert.NotNull(metadata);
        Assert.Equal(5401, metadata!.DurationSeconds);
        Assert.Equal("h264", metadata.Codec);
        Assert.Equal("1920x1080", metadata.Resolution);
        Assert.Equal(25, metadata.FrameRate);
    }

    [Fact]
    public void ParseFFprobeOutput_StreamDurationUsedWhenFormatMissing()
    {
        const string json = """
            {
                "streams": [
                    { "codec_type": "video", "codec_name": "vp9", "width": 1280, "height": 720,
                      "duration": "3600.75" }
                ]
            }
            """;

        var metadata = FileValidator.ParseFFprobeOutput(json);

        Assert.NotNull(metadata);
        Assert.Equal(3601, metadata!.DurationSeconds); // rounded up from .75
        Assert.Equal("vp9", metadata.Codec);
    }

    [Fact]
    public void ParseFFprobeOutput_NoDurationAnywhere_ReturnsZeroDuration()
    {
        const string json = """
            {
                "streams": [ { "codec_type": "video", "codec_name": "mpeg4", "width": 640, "height": 480 } ]
            }
            """;

        var metadata = FileValidator.ParseFFprobeOutput(json);

        Assert.NotNull(metadata);
        Assert.Equal(0, metadata!.DurationSeconds);
        Assert.Equal("mpeg4", metadata.Codec);
    }

    [Fact]
    public void ParseFFprobeOutput_NumericDurationJson_IsTolerated()
    {
        const string json = """
            { "streams": [ { "codec_type": "video", "codec_name": "h264" } ],
              "format": { "duration": 1800.5 } }
            """;

        var metadata = FileValidator.ParseFFprobeOutput(json);

        Assert.NotNull(metadata);
        Assert.Equal(1801, metadata!.DurationSeconds);
    }

    [Fact]
    public void ParseFFprobeOutput_NoVideoStream_ReturnsNull()
    {
        const string json = """{ "streams": [ { "codec_type": "audio", "codec_name": "mp3" } ] }""";
        Assert.Null(FileValidator.ParseFFprobeOutput(json));
    }

    [Fact]
    public void ParseFFprobeOutput_EmptyStreams_ReturnsNull()
    {
        const string json = """{ "streams": [] }""";
        Assert.Null(FileValidator.ParseFFprobeOutput(json));
    }
}

/// <summary>
/// Integration-level checks against the REAL ffprobe binary when it is on PATH
/// (silently pass on machines/CI images without it — the JSON parser itself is
/// fully covered by the tests above).
/// </summary>
public class FFprobeIntegrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ffprobe")]
    public void ResolveFFprobePath_NothingConfigured_FallsBackToPathName(string? configured)
    {
        var resolved = FileValidator.ResolveFFprobePath(
            configured, baseDirectory: "/any/install", fileExists: _ => false);

        Assert.Equal(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe", resolved);
    }

    [Fact]
    public void ResolveFFprobePath_BundledNextToAgent_IsAutoDetected()
    {
        var baseDirectory = OperatingSystem.IsWindows() ? "C:\\app\\agent" : "/app/agent";
        var bundledName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

        var resolved = FileValidator.ResolveFFprobePath(
            configured: null, baseDirectory, fileExists: path => path == Path.Combine(baseDirectory, bundledName));

        Assert.Equal(Path.Combine(baseDirectory, bundledName), resolved);
    }

    [Fact]
    public void ResolveFFprobePath_AbsoluteConfiguredPath_WinsOverBundled()
    {
        var absolute = OperatingSystem.IsWindows() ? "C:\\Tools\\ffprobe.exe" : "/opt/tools/ffprobe";

        var resolved = FileValidator.ResolveFFprobePath(
            configured: absolute, baseDirectory: "/any", fileExists: _ => true);

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void ResolveFFprobePath_RelativeOrBareConfigured_PassesThroughForPathLookup()
    {
        var configured = OperatingSystem.IsWindows() ? "C:\\ffprobe-bin\\ffprobe.exe" : "tools/ffprobe";

        var resolved = FileValidator.ResolveFFprobePath(
            configured: configured, baseDirectory: "/any", fileExists: _ => false);

        // Bare/relative names resolve against PATH at invocation time, not here.
        Assert.Equal(configured, resolved);
    }

    private static bool HasBinary(string name)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" };
        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim('"'), name))
            .Any(candidate => extensions.Any(ext => File.Exists(candidate + ext)));
    }

    [Fact]
    public async Task ExtractVideoMetadataAsync_GeneratedClip_ReadsRealDurationAndSource()
    {
        if (!HasBinary("ffmpeg") || !HasBinary("ffprobe"))
            return; // parse-level coverage lives in FFprobeParseTests

        var clipPath = Path.Combine(Path.GetTempPath(), $"la-ffprobe-{Guid.NewGuid():N}.mp4");
        try
        {
            var generated = await Task.Run(() =>
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -v error -f lavfi -i testsrc=duration=7:size=160x120:rate=10 -pix_fmt yuv420p \"{clipPath}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process == null)
                    return false;
                process.WaitForExit(30000);
                return process.ExitCode == 0 && File.Exists(clipPath);
            });

            if (!generated)
                return; // ffmpeg without lavfi support; nothing to assert

            var validator = new FileValidator(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileValidator>.Instance);
            var metadata = await validator.ExtractVideoMetadataAsync(clipPath);

            Assert.NotNull(metadata);
            Assert.Equal("ffprobe", metadata!.Source);
            Assert.True(metadata.DurationSeconds is >= 6 and <= 9,
                $"expected ~7s duration but got {metadata.DurationSeconds}s");
            Assert.Equal("h264", metadata.Codec);
            Assert.Equal("160x120", metadata.Resolution);
        }
        finally
        {
            if (File.Exists(clipPath))
                File.Delete(clipPath);
        }
    }

    [Fact]
    public async Task ExtractVideoMetadataAsync_GarbageVideoContent_FallsBackToUnknownWithoutHanging()
    {
        var garbagePath = Path.Combine(Path.GetTempPath(), $"la-garbage-{Guid.NewGuid():N}.mp4");
        try
        {
            var bytes = new byte[2 * 1024 * 1024];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(garbagePath, bytes);

            var validator = new FileValidator(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileValidator>.Instance);
            var metadata = await validator.ExtractVideoMetadataAsync(garbagePath);

            // Random bytes are not a decodable stream: must not throw, must not hang.
            Assert.NotNull(metadata);
            Assert.Equal(0, metadata!.DurationSeconds);
            Assert.Equal("unknown", metadata.Codec);
        }
        finally
        {
            if (File.Exists(garbagePath))
                File.Delete(garbagePath);
        }
    }
}
