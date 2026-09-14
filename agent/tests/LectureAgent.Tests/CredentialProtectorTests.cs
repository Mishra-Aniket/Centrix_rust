using LectureAgent.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LectureAgent.Tests;

public class CredentialProtectorTests : IDisposable
{
    private readonly string _directory;
    private readonly CredentialProtector _protector = new(NullLogger<CredentialProtector>.Instance);

    public CredentialProtectorTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"credprot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string PlaintextPath => Path.Combine(_directory, "google_credentials.json");
    private string ProtectedPath => PlaintextPath + ".protected";

    [Fact]
    public void ProtectInPlace_CreatesProtectedCopy_AndDeletesPlaintext()
    {
        File.WriteAllText(PlaintextPath, """{"installed":{"client_id":"abc","client_secret":"shh"}}""");

        _protector.ProtectInPlace(PlaintextPath);

        Assert.False(File.Exists(PlaintextPath), "plain-text credentials must be removed");
        Assert.True(File.Exists(ProtectedPath));
    }

    [Fact]
    public void OpenRead_AfterProtecting_RoundTripsTheOriginalContent()
    {
        const string original = """{"installed":{"client_id":"abc","client_secret":"shh"}}""";
        File.WriteAllText(PlaintextPath, original);

        _protector.ProtectInPlace(PlaintextPath);

        using var stream = _protector.OpenRead(PlaintextPath);
        using var reader = new StreamReader(stream);
        Assert.Equal(original, reader.ReadToEnd());
    }

    [Fact]
    public void OpenRead_PlaintextStillSupported()
    {
        const string original = """{"installed":{"client_id":"abc"}}""";
        File.WriteAllText(PlaintextPath, original);

        using var stream = _protector.OpenRead(PlaintextPath);
        using var reader = new StreamReader(stream);
        Assert.Equal(original, reader.ReadToEnd());
    }

    [Fact]
    public void ProtectInPlace_AlreadyProtected_RemovesStrayPlaintext()
    {
        File.WriteAllText(PlaintextPath, """{"installed":{"client_id":"abc"}}""");
        _protector.ProtectInPlace(PlaintextPath);

        // A tool or user re-dropped a plain-text copy; the next startup must clean it.
        File.WriteAllText(PlaintextPath, """{"installed":{"client_id":"abc"}}""");
        _protector.ProtectInPlace(PlaintextPath);

        Assert.False(File.Exists(PlaintextPath));
        Assert.True(File.Exists(ProtectedPath));
    }

    [Fact]
    public void ProtectInPlace_MissingFile_IsANoOp()
    {
        _protector.ProtectInPlace(Path.Combine(_directory, "does-not-exist.json"));
        // Nothing thrown, nothing created.
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ProtectInPlace_CorruptExistingProtectedFile_IsRepairedFromPlaintext()
    {
        const string original = """{"installed":{"client_id":"abc"}}""";
        File.WriteAllText(PlaintextPath, original);
        File.WriteAllText(ProtectedPath, "GARBAGE-NOT-A-VALID-FORMAT");

        // Self-healing: the corrupt protected copy is rewritten, the original is kept
        // until the new protected copy verifies.
        _protector.ProtectInPlace(PlaintextPath);

        Assert.False(File.Exists(PlaintextPath));
        using var stream = _protector.OpenRead(PlaintextPath);
        using var reader = new StreamReader(stream);
        Assert.Equal(original, reader.ReadToEnd());
    }
}
