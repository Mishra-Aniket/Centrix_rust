namespace LectureAgent.Configuration;

/// <summary>
/// Resolves the folders the agent reads and writes at runtime.
/// On Windows the agent runs as a LocalSystem service while the desktop app runs as the
/// signed-in user, so settings, database and logs live in a shared ProgramData folder both
/// accounts can reach. Every other platform keeps them beside the executable as before.
/// </summary>
public static class AgentPaths
{
    /// <summary>C:\ProgramData\LectureAgent on Windows, null on macOS and Linux.</summary>
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

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrWhiteSpace(programData)
            ? null
            : Path.Combine(programData, "LectureAgent");
    }
}
