using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace LectureAgent.Desktop;

/// <summary>
/// Read/write access to the settings file the installer creates in ProgramData.
/// Unknown keys are preserved, so this layer can be edited without losing anything the
/// agent added.
/// </summary>
internal sealed class AgentSettings
{
    private const int DefaultPort = 5200;

    private readonly JsonObject _root;

    private AgentSettings(JsonObject root)
    {
        _root = root;
    }

    internal static AgentSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var text = File.ReadAllText(AppPaths.SettingsFile);
                if (JsonNode.Parse(text) is JsonObject parsed)
                {
                    return new AgentSettings(parsed);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A missing or corrupt file falls back to defaults; Save() rewrites it cleanly.
        }

        return new AgentSettings(new JsonObject());
    }

    internal string ApiKey
    {
        get => GetString("Auth", "ApiKey");
        set => SetValue(value, "Auth", "ApiKey");
    }

    internal string CenterId
    {
        get => GetString("Agent", "CenterId");
        set => SetValue(value, "Agent", "CenterId");
    }

    internal string RoomId
    {
        get => GetString("Agent", "RoomId");
        set => SetValue(value, "Agent", "RoomId");
    }

    internal string DeviceId
    {
        get => GetString("Agent", "DeviceId");
        set => SetValue(value, "Agent", "DeviceId");
    }

    internal string MonitorFolder
    {
        get => GetString("FileWatcher", "MonitorFolder");
        set => SetValue(value, "FileWatcher", "MonitorFolder");
    }

    /// <summary>
    /// Optional separate folder for notes (PDF/PPT). Blank keeps everything in the
    /// recordings folder.
    /// </summary>
    internal string NotesMonitorFolder
    {
        get => GetString("FileWatcher", "NotesFolder");
        set => SetValue(value, "FileWatcher", "NotesFolder");
    }

    internal bool GoogleDriveEnabled
    {
        get => GetBool("GoogleDrive", "Enabled") ?? true;
        set => SetValue(value, "GoogleDrive", "Enabled");
    }

    internal string GoogleDriveCredentialsPath
    {
        get => GetString("GoogleDrive", "CredentialsPath");
        set => SetValue(value, "GoogleDrive", "CredentialsPath");
    }

    internal string GoogleDriveRootFolder
    {
        get => GetString("GoogleDrive", "RootFolderPath");
        set => SetValue(value, "GoogleDrive", "RootFolderPath");
    }

    internal bool YouTubeEnabled
    {
        get => GetBool("YouTube", "Enabled") ?? false;
        set => SetValue(value, "YouTube", "Enabled");
    }

    internal bool YouTubeAutoPublish
    {
        get => GetBool("YouTube", "AutoPublishAfterDriveUpload") ?? false;
        set => SetValue(value, "YouTube", "AutoPublishAfterDriveUpload");
    }

    internal int Port
    {
        get
        {
            var url = GetString("Kestrel", "Endpoints", "Http", "Url");
            return Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Port > 0
                ? parsed.Port
                : DefaultPort;
        }
        set => SetValue($"http://0.0.0.0:{value}", "Kestrel", "Endpoints", "Http", "Url");
    }

    /// <summary>Loopback address the app itself talks to.</summary>
    internal string LocalBaseUrl => $"http://127.0.0.1:{Port}";

    internal void Save()
    {
        // The service runs as LocalSystem and this app as the user, so both the database
        // and the Drive token must sit in the shared, writable ProgramData folder.
        SetValue(true, "Auth", "Enabled");
        SetValue(true, "FileWatcher", "EnableFileWatcher");
        // New installs use the Centrix database name. Keep an existing legacy database
        // in place so opening Settings after an upgrade never makes old lecture data disappear.
        var databasePath = Path.Combine(AppPaths.DataDirectory, "centrix.db");
        var legacyDatabasePath = Path.Combine(AppPaths.DataDirectory, "lecture_agent.db");
        if (!File.Exists(databasePath) && File.Exists(legacyDatabasePath))
        {
            databasePath = legacyDatabasePath;
        }

        SetValue(databasePath, "Database", "SqlitePath");
        SetValue(Path.Combine(AppPaths.DataDirectory, "google-drive-token"), "GoogleDrive", "TokenPath");
        SetValue(Path.Combine(AppPaths.DataDirectory, "youtube-token"), "YouTube", "TokenPath");

        Directory.CreateDirectory(AppPaths.SharedRoot);
        Directory.CreateDirectory(AppPaths.DataDirectory);

        // JsonNode values created through JsonValue.Create require an explicit
        // resolver when these options become read-only on .NET 8. Without it,
        // saving setup settings throws before the Google authorization helper
        // can start.
        var json = _root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });
        var temporaryFile = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporaryFile, json);
        File.Move(temporaryFile, AppPaths.SettingsFile, overwrite: true);
    }

    internal static string GenerateApiKey() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private string GetString(params string[] path)
    {
        var node = Resolve(path);
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
    }

    private bool? GetBool(params string[] path)
    {
        var node = Resolve(path);
        return node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
    }

    private JsonNode? Resolve(string[] path)
    {
        JsonNode? node = _root;
        foreach (var segment in path)
        {
            if (node is not JsonObject container)
            {
                return null;
            }

            node = container[segment];
        }

        return node;
    }

    private void SetValue<T>(T value, params string[] path)
    {
        var container = _root;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (container[path[i]] is JsonObject existing)
            {
                container = existing;
            }
            else
            {
                var created = new JsonObject();
                container[path[i]] = created;
                container = created;
            }
        }

        container[path[^1]] = JsonValue.Create(value);
    }
}
