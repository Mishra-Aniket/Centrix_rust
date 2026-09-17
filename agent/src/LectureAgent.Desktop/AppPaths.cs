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
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(InstallDirectory, "..", "agent", "CentrixAgent.exe")),
                Path.GetFullPath(Path.Combine(InstallDirectory, "..", "agent", "LectureAgent.exe")),
                Path.GetFullPath(Path.Combine(InstallDirectory, "..", "agent", "Centrix.exe")),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Centrix", "agent", "CentrixAgent.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Centrix", "agent", "LectureAgent.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Centrix", "agent", "Centrix.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LectureAgentApp", "agent", "LectureAgent.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }
    }

    internal static string SharedRoot
    {
        get
        {
            // 1. Explicit environment override
            var envRoot = Environment.GetEnvironmentVariable("CENTRIX_SHARED_ROOT")
                       ?? Environment.GetEnvironmentVariable("LA_SETUP_ROOT");
            if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
            {
                return envRoot;
            }

            // 2. Check if installed in a custom directory (<root>\app -> parent is <root>)
            try
            {
                var parentDir = Path.GetFullPath(Path.Combine(InstallDirectory, ".."));
                if (File.Exists(Path.Combine(parentDir, "appsettings.json")) || Directory.Exists(Path.Combine(parentDir, "config")))
                {
                    return parentDir;
                }
            }
            catch { }

            // 3. Fall back to standard ProgramData\Centrix
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var centrixRoot = Path.Combine(programData, "Centrix");
            var legacyRoot = Path.Combine(programData, "LectureAgent");

            try
            {
                if (Directory.Exists(legacyRoot) && !Directory.Exists(Path.Combine(centrixRoot, "config")))
                {
                    Directory.CreateDirectory(centrixRoot);
                    foreach (var dir in new[] { "config", "data", "logs" })
                    {
                        var src = Path.Combine(legacyRoot, dir);
                        var dst = Path.Combine(centrixRoot, dir);
                        if (Directory.Exists(src) && !Directory.Exists(dst))
                        {
                            Directory.CreateDirectory(dst);
                            foreach (var f in Directory.GetFiles(src))
                            {
                                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                            }
                        }
                    }
                    var legacySettings = Path.Combine(legacyRoot, "appsettings.json");
                    var targetSettings = Path.Combine(centrixRoot, "appsettings.json");
                    if (File.Exists(legacySettings) && !File.Exists(targetSettings))
                    {
                        File.Copy(legacySettings, targetSettings, true);
                    }
                }
            }
            catch { }

            return centrixRoot;
        }
    }

    internal static string SettingsFile { get; } = Path.Combine(SharedRoot, "appsettings.json");

    internal static string DataDirectory { get; } = Path.Combine(SharedRoot, "data");

    internal static string LogDirectory { get; } = Path.Combine(SharedRoot, "logs");

    internal static string WebViewUserData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Centrix",
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
