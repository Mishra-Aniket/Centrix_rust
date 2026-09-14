namespace LectureAgent.Desktop;

/// <summary>
/// Install layout: the installer puts the service under &lt;install&gt;\agent and this app
/// under &lt;install&gt;\app, while everything writable lives in ProgramData so the
/// LocalSystem service and the signed-in user share it.
/// </summary>
internal static class AppPaths
{
    internal static string InstallDirectory { get; } = AppContext.BaseDirectory;

    internal static string AgentExecutable { get; } =
        Path.GetFullPath(Path.Combine(InstallDirectory, "..", "agent", "LectureAgent.exe"));

    internal static string SharedRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LectureAgent");

    internal static string SettingsFile { get; } = Path.Combine(SharedRoot, "appsettings.json");

    internal static string DataDirectory { get; } = Path.Combine(SharedRoot, "data");

    internal static string LogDirectory { get; } = Path.Combine(SharedRoot, "logs");

    internal static string WebViewUserData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LectureAgent",
        "WebView2");

    internal static string AppIcon { get; } = Path.Combine(InstallDirectory, "app.ico");
}
