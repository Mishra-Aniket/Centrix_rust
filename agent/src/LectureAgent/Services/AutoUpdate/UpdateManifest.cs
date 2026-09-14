namespace LectureAgent.Services.AutoUpdate;

using System.Text.Json;

/// <summary>
/// Update manifest served by the distribution endpoint, e.g.
/// {
///   "version": "1.0.1",
///   "downloadUrl": "https://releases.example.com/LectureAgent-1.0.1-win-x64.zip",
///   "sha256": "a3f...",
///   "notes": "Fixes upload retry bug",
///   "mandatory": false
/// }
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Sha256 { get; init; }
    public string? Notes { get; init; }
    public bool Mandatory { get; init; }

    /// <summary>
    /// Parses the manifest JSON. Returns null (instead of throwing) on malformed
    /// input so a broken release endpoint can never crash the agent.
    /// </summary>
    public static UpdateManifest? FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var version = root.TryGetProperty("version", out var versionEl) ? versionEl.GetString()?.Trim() : null;
            var downloadUrl = root.TryGetProperty("downloadUrl", out var urlEl) ? urlEl.GetString()?.Trim() : null;

            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(downloadUrl))
                return null;

            return new UpdateManifest
            {
                Version = version,
                DownloadUrl = downloadUrl,
                Sha256 = root.TryGetProperty("sha256", out var shaEl) ? shaEl.GetString()?.Trim() : null,
                Notes = root.TryGetProperty("notes", out var notesEl) ? notesEl.GetString() : null,
                Mandatory = root.TryGetProperty("mandatory", out var mandatoryEl) && mandatoryEl.ValueKind == JsonValueKind.True
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Semver-ish comparison with prerelease handling: "1.0.0-alpha" is older than
/// "1.0.0", and "1.0.1-alpha" is newer than "1.0.0". Pure and unit-testable.
/// </summary>
public static class UpdateVersion
{
    public static bool IsNewerThan(string current, string candidate)
    {
        var (currentCore, currentPre) = Split(current);
        var (candidateCore, candidatePre) = Split(candidate);

        if (!Version.TryParse(currentCore, out var currentVersion)
            || !Version.TryParse(candidateCore, out var candidateVersion))
            return false;

        var comparison = currentVersion.CompareTo(candidateVersion);
        if (comparison != 0)
            return comparison < 0;

        return (currentPre, candidatePre) switch
        {
            (null, null) => false,
            (null, _) => false,       // stable current vs prerelease candidate: not newer
            (_, null) => true,        // prerelease current vs stable candidate: newer
            (_, _) => string.CompareOrdinal(candidatePre, currentPre) > 0
        };
    }

    private static (string Core, string? Prerelease) Split(string? version)
    {
        var value = version?.Trim() ?? string.Empty;
        var dashIndex = value.IndexOf('-');
        return dashIndex < 0
            ? (value, null)
            : (value[..dashIndex], value[(dashIndex + 1)..]);
    }
}
