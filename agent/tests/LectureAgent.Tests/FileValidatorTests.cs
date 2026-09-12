using LectureAgent.Infrastructure.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class FileValidatorTests
{
    private readonly FileValidator _validator = new(NullLogger<FileValidator>.Instance);

    [Fact]
    public async Task ValidateFileAsync_NonExistentFile_ReturnsInvalid()
    {
        var (valid, error) = await _validator.ValidateFileAsync("/non/existent/file.mp4");
        Assert.False(valid);
        Assert.Equal("File does not exist", error);
    }

    [Fact]
    public async Task ValidateFileAsync_UnsupportedExtension_ReturnsInvalid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var renamed = Path.ChangeExtension(tempFile, ".txt");
            File.Move(tempFile, renamed);
            try
            {
                var (valid, error) = await _validator.ValidateFileAsync(renamed);
                Assert.False(valid);
                Assert.Contains("Unsupported file type", error);
            }
            finally
            {
                if (File.Exists(renamed)) File.Delete(renamed);
            }
        }
        catch
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsFileStableAsync_EmptyFile_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Empty file has size 0
            var isStable = await _validator.IsFileStableAsync(tempFile, stabilityCheckMs: 100);
            Assert.False(isStable);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
