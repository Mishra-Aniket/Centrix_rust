using LectureAgent.Services.AutoUpdate;
using Xunit;

namespace LectureAgent.Tests;

public class UpdateManifestTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.1", "1.0.0", false)]
    [InlineData("1.10.0", "1.9.0", false)] // numeric, not lexical comparison
    [InlineData("1.0.0-alpha", "1.0.0", true)] // prerelease -> stable is an upgrade
    [InlineData("1.0.0", "1.0.0-alpha", false)] // stable -> prerelease is a downgrade
    [InlineData("1.0.0-alpha", "1.0.0-beta", true)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", false)]
    [InlineData("1.0.0-alpha", "1.0.1-alpha", true)]
    public void IsNewerThan_HandlesCoreAndPrereleaseVersions(string current, string candidate, bool expected)
    {
        Assert.Equal(expected, UpdateVersion.IsNewerThan(current, candidate));
    }

    [Fact]
    public void IsNewerThan_GarbageInput_ReturnsFalse()
    {
        Assert.False(UpdateVersion.IsNewerThan("not-a-version", "1.0.0"));
        Assert.False(UpdateVersion.IsNewerThan("1.0.0", ""));
        Assert.False(UpdateVersion.IsNewerThan("", "1.0.0"));
    }

    [Fact]
    public void FromJson_ValidManifest_ParsesAllFields()
    {
        const string json = """
            {
                "version": "1.0.1",
                "downloadUrl": "https://releases.example.com/LectureAgent-1.0.1.zip",
                "sha256": "abc123",
                "notes": "Fixes upload retry bug",
                "mandatory": true
            }
            """;

        var manifest = UpdateManifest.FromJson(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.0.1", manifest!.Version);
        Assert.Equal("https://releases.example.com/LectureAgent-1.0.1.zip", manifest.DownloadUrl);
        Assert.Equal("abc123", manifest.Sha256);
        Assert.Equal("Fixes upload retry bug", manifest.Notes);
        Assert.True(manifest.Mandatory);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{}")]
    [InlineData("{\"version\": \"1.0.0\"}")]
    [InlineData("{\"downloadUrl\": \"https://x/y.zip\"}")]
    [InlineData("[]")]
    public void FromJson_MalformedManifest_ReturnsNull(string json)
    {
        Assert.Null(UpdateManifest.FromJson(json));
    }
}
