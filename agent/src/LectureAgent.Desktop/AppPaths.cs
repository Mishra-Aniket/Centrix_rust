namespace LectureAgent.Desktop;

/// <summary>
/// Install layout: the installer puts the service under &lt;install&gt;\agent and this app
/// under &lt;install&gt;\app, while everything writable lives in ProgramData so the
/// LocalSystem service and the signed-in user share it.
/// </summary>
internal static class AppPaths
{
    internal static string InstallDirectory { get; } = AppContext.BaseDirectory;

    internal static string AgentExecutable
    {
        get
        {
            var beside = Path.GetFullPath(Path.Combine(InstallDirectory, "..", "agent", "LectureAgent.exe"));
            if (File.Exists(beside))
            {
                return beside;
            }

            var programDataCentrix = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Centrix", "agent", "LectureAgent.exe");
            if (File.Exists(programDataCentrix))
            {
                return programDataCentrix;
            }

            var programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LectureAgentApp", "agent", "LectureAgent.exe");
            if (File.Exists(programData))
            {
                return programData;
            }

            return beside;
        }
    }

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

    internal static string AppIcon
    {
        get
        {
            var inAssets = Path.Combine(InstallDirectory, "Assets", "app.ico");
            if (File.Exists(inAssets)) return inAssets;
            var beside = Path.Combine(InstallDirectory, "app.ico");
            if (File.Exists(beside)) return beside;
            return inAssets;
        }
    }

    internal static string AppLogo
    {
        get
        {
            var inAssets = Path.Combine(InstallDirectory, "Assets", "app.png");
            if (File.Exists(inAssets)) return inAssets;
            var beside = Path.Combine(InstallDirectory, "app.png");
            if (File.Exists(beside)) return beside;
            return inAssets;
        }
    }

    internal static Icon LoadIcon()
    {
        try
        {
            if (File.Exists(AppIcon))
            {
                return new Icon(AppIcon);
            }

            var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("LectureAgent.Desktop.Assets.app.ico");
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    internal static Image? LoadLogoImage()
    {
        try
        {
            if (File.Exists(AppLogo))
            {
                return Image.FromFile(AppLogo);
            }

            var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("LectureAgent.Desktop.Assets.app.png");
            if (stream != null)
            {
                return Image.FromStream(stream);
            }

            if (File.Exists(AppIcon))
            {
                using var ico = new Icon(AppIcon, 64, 64);
                return ico.ToBitmap();
            }
        }
        catch
        {
        }

        return null;
    }
}
