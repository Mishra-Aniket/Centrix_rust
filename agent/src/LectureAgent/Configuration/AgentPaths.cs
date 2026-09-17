namespace LectureAgent.Configuration;

/// <summary>
/// Resolves the folders the agent reads and writes at runtime.
/// On Windows the agent runs as a LocalSystem service while the desktop app runs as the
/// signed-in user, so settings, database and logs live in a shared ProgramData folder both
/// accounts can reach. Every other platform keeps them beside the executable as before.
/// </summary>
public static class AgentPaths
{
    /// <summary>C:\ProgramData\Centrix on Windows, null on macOS and Linux.</summary>
    public static string? SharedRoot { get; } = ResolveSharedRoot();

    /// <summary>Root for data written at runtime (database, Drive token).</summary>
    public static string DataRoot => SharedRoot ?? AppContext.BaseDirectory;

    public static string LogDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>
    /// Settings file the installer and desktop app write. Layered on top of the
    /// appsettings.json shipped next to the executable, which stays read-only.
    /// </summary>
    public static string? SharedSettingsFile =>
        SharedRoot is null ? null : Path.Combine(SharedRoot, "appsettings.json");

    private static string? ResolveSharedRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // 1. Explicit environment override
        var envRoot = Environment.GetEnvironmentVariable("CENTRIX_SHARED_ROOT")
                   ?? Environment.GetEnvironmentVariable("LA_SETUP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return envRoot;
        }

        // 2. Check if installed in a custom directory (<root>\agent -> parent is <root>)
        try
        {
            var parentDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            if (File.Exists(Path.Combine(parentDir, "appsettings.json")) || Directory.Exists(Path.Combine(parentDir, "config")))
            {
                return parentDir;
            }
        }
        catch { }

        // 3. Fall back to standard ProgramData\Centrix
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
        {
            return null;
        }

        var centrixRoot = Path.Combine(programData, "Centrix");
        var legacyRoot = Path.Combine(programData, "LectureAgent");

        // Seamless migration: If legacy LectureAgent data exists and Centrix doesn't have it yet, copy it over
        try
        {
            if (Directory.Exists(legacyRoot))
            {
                Directory.CreateDirectory(centrixRoot);
                MigrateLegacyFiles(legacyRoot, centrixRoot);
            }
        }
        catch
        {
            // Non-fatal if legacy migration encounters locked file
        }

        return centrixRoot;
    }

    private static void MigrateLegacyFiles(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                try { File.Copy(file, dest, overwrite: false); } catch { }
            }
        }

        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            var subName = Path.GetFileName(sub);
            // Skip binary subdirectories if any, only copy config, data, logs
            if (subName.Equals("agent", StringComparison.OrdinalIgnoreCase) ||
                subName.Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destSub = Path.Combine(targetDir, subName);
            Directory.CreateDirectory(destSub);
            MigrateLegacyFiles(sub, destSub);
        }
    }
}
