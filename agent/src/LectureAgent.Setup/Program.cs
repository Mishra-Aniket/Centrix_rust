using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
#if NET8_0_WINDOWS
using System.Windows.Forms;
#endif

namespace LectureAgent.Setup;

// Single-file Windows installer: extracts the bundled agent + desktop app into
// C:\ProgramData\LectureAgentApp (writable without admin rights) and starts the
// desktop app, whose one-time setup wizard finishes configuration, the Windows
// service (UAC), the firewall rule and the Google account connection.
internal static class Program
{
    internal const string InstallDirName = "Centrix";

    [STAThread]
    private static void Main(string[] args)
    {
        var root = Environment.GetEnvironmentVariable("CENTRIX_SHARED_ROOT")
            ?? Environment.GetEnvironmentVariable("LA_SETUP_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                InstallDirName);
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith("/path:", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--path=", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-path:", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg.Substring(arg.IndexOfAny(new[] { ':', '=' }) + 1).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(val)) root = Path.GetFullPath(val);
            }
            else if (arg.StartsWith("/dir:", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("-dir:", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg.Substring(arg.IndexOfAny(new[] { ':', '=' }) + 1).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(val)) root = Path.GetFullPath(val);
            }
        }

        var isUninstall = args.Any(a =>
            a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/u", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-u", StringComparison.OrdinalIgnoreCase));

        var quiet = args.Any(a =>
            a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-q", StringComparison.OrdinalIgnoreCase));

        if (isUninstall)
        {
            PerformUninstall(root, quiet);
            return;
        }

        var noLaunch = Environment.GetEnvironmentVariable("LA_SETUP_NO_LAUNCH") == "1"
            || args.Any(a => a.Equals("/nolaunch", StringComparison.OrdinalIgnoreCase) || a.Equals("--no-launch", StringComparison.OrdinalIgnoreCase));
        var noUi = Environment.GetEnvironmentVariable("LA_SETUP_NO_UI") == "1"
            || quiet
            || !OperatingSystem.IsWindows();

#if NET8_0_WINDOWS
        if (!noUi)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new InstallForm(root, !noLaunch));
            return;
        }
#endif

        var error = ExtractAndLaunch(root, !noLaunch);
        if (error != null)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>Returns null on success, or a user-readable error message.</summary>
    /// <summary>Returns null on success, or a user-readable error message.</summary>
    internal static string? ExtractAndLaunch(string root, bool launch)
    {
        string? error = Extract(root);
        if (error != null)
        {
            return error;
        }

        // Provision shared data, tokens, and config in ProgramData\Centrix
        SetupSharedData(root);

        // Configure Windows Firewall rule for port 5200
        ConfigureFirewall();

        var app = Path.Combine(root, "app", "Centrix.exe");
        if (!File.Exists(app))
        {
            app = Path.Combine(root, "app", "LectureAgentApp.exe");
        }

        var agent = Path.Combine(root, "agent", "CentrixAgent.exe");
        if (!File.Exists(agent))
        {
            agent = Path.Combine(root, "agent", "LectureAgent.exe");
        }
        if (!File.Exists(agent))
        {
            agent = Path.Combine(root, "agent", "Centrix.exe");
        }

        // Install and start Centrix background service immediately
        InstallAndStartService(agent);

        // Register in Windows Add/Remove Programs (Registry) & create uninstall.bat
        RegisterUninstaller(root, app);

        CreateShortcuts(app);

        if (launch && File.Exists(app))
        {
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
        }

        return null;
    }

    private static void SetupSharedData(string root)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            try
            {
                Environment.SetEnvironmentVariable("CENTRIX_SHARED_ROOT", root, EnvironmentVariableTarget.Machine);
            }
            catch { }

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "config"));
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));

            var agentDir = Path.Combine(root, "agent");
            var sourceConfig = Path.Combine(agentDir, "config");
            var sourceData = Path.Combine(agentDir, "data");
            var sourceSettings = Path.Combine(agentDir, "appsettings.json");

            // Copy credentials if target does not already have it
            if (Directory.Exists(sourceConfig))
            {
                foreach (var file in Directory.GetFiles(sourceConfig))
                {
                    var target = Path.Combine(root, "config", Path.GetFileName(file));
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target, true);
                    }
                }
            }

            // Copy pre-authenticated Google Drive tokens
            if (Directory.Exists(sourceData))
            {
                CopyDirectoryRecursive(sourceData, Path.Combine(root, "data"));
            }

            // Copy appsettings.json if target doesn't have it
            var targetSettings = Path.Combine(root, "appsettings.json");
            if (!File.Exists(targetSettings) && File.Exists(sourceSettings))
            {
                File.Copy(sourceSettings, targetSettings, true);
            }

            // Migrate from legacy C:\ProgramData\LectureAgent if present
            var legacyRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LectureAgent");
            if (Directory.Exists(legacyRoot))
            {
                var legacyConfig = Path.Combine(legacyRoot, "config");
                if (Directory.Exists(legacyConfig))
                {
                    foreach (var file in Directory.GetFiles(legacyConfig))
                    {
                        var target = Path.Combine(root, "config", Path.GetFileName(file));
                        if (!File.Exists(target))
                        {
                            try { File.Copy(file, target, false); } catch { }
                        }
                    }
                }
                var legacyData = Path.Combine(legacyRoot, "data");
                if (Directory.Exists(legacyData))
                {
                    CopyDirectoryRecursive(legacyData, Path.Combine(root, "data"));
                }
                var legacySettings = Path.Combine(legacyRoot, "appsettings.json");
                if (File.Exists(legacySettings) && !File.Exists(targetSettings))
                {
                    try { File.Copy(legacySettings, targetSettings, false); } catch { }
                }
            }
        }
        catch
        {
            // Non-fatal if folder seeding encounters locked file
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile, true);
            }
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
        }
    }

    private static void InstallAndStartService(string agentExe)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(agentExe)) return;

        try
        {
            var script = $"""
                chcp 437 >nul
                net stop LectureAgent >nul 2>&1
                sc delete LectureAgent >nul 2>&1
                net stop Centrix >nul 2>&1
                sc delete Centrix >nul 2>&1
                sc create Centrix binpath= "\"{agentExe}\"" start= auto
                sc description Centrix "Centrix - Automatic Classroom Lecture Uploader"
                sc failure Centrix reset= 86400 actions= restart/5000/restart/10000/restart/60000
                sc sdset Centrix D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;RPWPDTLO;;;IU)
                sc start Centrix
                """;

            var scriptPath = Path.Combine(Path.GetTempPath(), $"centrix-svc-install-{Environment.ProcessId}.cmd");
            File.WriteAllText(scriptPath, script);
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch
        {
        }
    }

    private static void RegisterUninstaller(string root, string appExe)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            CreateUninstallScripts(root);

            var uninstallBat = Path.Combine(root, "uninstall.bat");
            var script = $"""
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "DisplayName" /d "Centrix" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "DisplayVersion" /d "1.0.4" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "Publisher" /d "PhysicsWallah" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "InstallLocation" /d "{root}" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "UninstallString" /d "cmd.exe /c \"{uninstallBat}\"" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "QuietUninstallString" /d "cmd.exe /c \"{uninstallBat}\" /quiet" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "DisplayIcon" /d "{appExe},0" /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "NoModify" /t REG_DWORD /d 1 /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "NoRepair" /t REG_DWORD /d 1 /f >nul 2>&1
                reg add "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /v "EstimatedSize" /t REG_DWORD /d 256000 /f >nul 2>&1
                """;

            var scriptPath = Path.Combine(Path.GetTempPath(), $"centrix-reg-{Environment.ProcessId}.cmd");
            File.WriteAllText(scriptPath, script);
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch
        {
        }
    }

    private static void CreateUninstallScripts(string root)
    {
        var bat = $"""
            @echo off
            title Centrix Uninstaller
            echo ========================================================
            echo           Uninstalling Centrix (PhysicsWallah)
            echo ========================================================
            echo.
            echo [1/4] Stopping background service and processes...
            net stop Centrix >nul 2>&1
            net stop LectureAgent >nul 2>&1
            sc delete Centrix >nul 2>&1
            sc delete LectureAgent >nul 2>&1
            taskkill /F /IM Centrix.exe >nul 2>&1
            taskkill /F /IM CentrixAgent.exe >nul 2>&1
            taskkill /F /IM LectureAgent.exe >nul 2>&1
            taskkill /F /IM LectureAgentApp.exe >nul 2>&1

            echo [2/4] Removing firewall rules...
            netsh advfirewall firewall delete rule name="Centrix Dashboard" >nul 2>&1
            netsh advfirewall firewall delete rule name="Centrix Lecture Agent" >nul 2>&1

            echo [3/4] Removing shortcuts and registry entries...
            del /F /Q "%PUBLIC%\Desktop\Centrix.lnk" >nul 2>&1
            del /F /Q "%USERPROFILE%\Desktop\Centrix.lnk" >nul 2>&1
            del /F /Q "%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Centrix.lnk" >nul 2>&1
            del /F /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk" >nul 2>&1
            del /F /Q "%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk" >nul 2>&1

            reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /f >nul 2>&1
            reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /f >nul 2>&1

            echo [4/4] Removing program files...
            if exist "C:\ProgramData\LectureAgent" rmdir /S /Q "C:\ProgramData\LectureAgent" >nul 2>&1

            if "%1"=="/quiet" goto done
            echo.
            echo Centrix has been completely uninstalled.
            timeout /t 3 /nobreak >nul
            :done
            (goto) 2>nul & rmdir /s /q "{root}"
            """;

        try
        {
            File.WriteAllText(Path.Combine(root, "uninstall.bat"), bat);
        }
        catch { }
    }

    private static void ConfigureFirewall()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c netsh advfirewall firewall delete rule name=\"Centrix Dashboard\" & netsh advfirewall firewall delete rule name=\"Centrix Lecture Agent\" & netsh advfirewall firewall add rule name=\"Centrix Dashboard\" dir=in action=allow protocol=TCP localport=5200")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch
        {
        }
    }

    private static void CreateShortcuts(string targetExe)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(targetExe))
        {
            return;
        }

        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);

            var desktopLnk = Path.Combine(desktopDir, "Centrix.lnk");
            var startMenuLnk = Path.Combine(programsDir, "Centrix.lnk");
            var startupLnk = Path.Combine(startupDir, "Centrix.lnk");

            CreateShortcutPowerShell(desktopLnk, targetExe);
            CreateShortcutPowerShell(startMenuLnk, targetExe);
            CreateShortcutPowerShell(startupLnk, targetExe);
        }
        catch
        {
            // Non-fatal if shortcut creation fails
        }
    }

    private static void CreateShortcutPowerShell(string lnkPath, string targetExe)
    {
        try
        {
            var workDir = Path.GetDirectoryName(targetExe) ?? "";
            var script = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{lnkPath.Replace("'", "''")}'); $s.TargetPath = '{targetExe.Replace("'", "''")}'; $s.WorkingDirectory = '{workDir.Replace("'", "''")}'; $s.IconLocation = '{targetExe.Replace("'", "''")},0'; $s.Description = 'Centrix - Automatic Lecture Uploader'; $s.Save()";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch
        {
        }
    }

    private static void StopRunningProcesses()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c net stop Centrix & net stop LectureAgent")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }

            var processNames = new[] { "Centrix", "CentrixAgent", "LectureAgent", "LectureAgentApp" };
            foreach (var name in processNames)
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(2000);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static string? Extract(string root)
    {
        var payloadStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LectureAgent.Setup.payload.zip");
        if (payloadStream == null)
        {
            return "Setup payload is missing from this exe. Rebuild the installer.";
        }

        try
        {
            StopRunningProcesses();
            Directory.CreateDirectory(root);
            using (payloadStream)
            {
                ZipFile.ExtractToDirectory(payloadStream, root, overwriteFiles: true);
            }

            return null;
        }
        catch (IOException ex)
        {
            return "Could not replace the program files (" + ex.GetType().Name + ").\n\n" +
                "The Centrix service is probably still running.\n" +
                "Open the Centrix app, stop the service, then run this setup again " +
                "(or restart the PC first).";
        }
        catch (Exception ex)
        {
            return "Installation failed: " + ex.Message;
        }
    }

    internal static void PerformUninstall(string root, bool quiet)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            StopRunningProcesses();

            var uninstallBat = Path.Combine(root, "uninstall.bat");
            if (File.Exists(uninstallBat))
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{uninstallBat}\" /quiet")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(10000);
            }
            else
            {
                var script = $"""
                    chcp 437 >nul
                    net stop Centrix >nul 2>&1
                    net stop LectureAgent >nul 2>&1
                    sc delete Centrix >nul 2>&1
                    sc delete LectureAgent >nul 2>&1
                    taskkill /F /IM Centrix.exe >nul 2>&1
                    taskkill /F /IM CentrixAgent.exe >nul 2>&1
                    taskkill /F /IM LectureAgent.exe >nul 2>&1
                    taskkill /F /IM LectureAgentApp.exe >nul 2>&1
                    netsh advfirewall firewall delete rule name="Centrix Dashboard" >nul 2>&1
                    netsh advfirewall firewall delete rule name="Centrix Lecture Agent" >nul 2>&1
                    del /F /Q "%PUBLIC%\Desktop\Centrix.lnk" >nul 2>&1
                    del /F /Q "%USERPROFILE%\Desktop\Centrix.lnk" >nul 2>&1
                    del /F /Q "%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Centrix.lnk" >nul 2>&1
                    del /F /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk" >nul 2>&1
                    del /F /Q "%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk" >nul 2>&1
                    reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /f >nul 2>&1
                    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Centrix" /f >nul 2>&1
                    if exist "C:\ProgramData\LectureAgent" rmdir /S /Q "C:\ProgramData\LectureAgent" >nul 2>&1
                    rmdir /S /Q "{root}" >nul 2>&1
                    """;

                var scriptPath = Path.Combine(Path.GetTempPath(), $"centrix-uninst-{Environment.ProcessId}.cmd");
                File.WriteAllText(scriptPath, script);
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(10000);
                }
                finally
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }

            if (!quiet)
            {
#if NET8_0_WINDOWS
                MessageBox.Show("Centrix has been completely uninstalled from your PC.", "Centrix",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
                Console.WriteLine("Centrix has been completely uninstalled from your PC.");
#endif
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
#if NET8_0_WINDOWS
                MessageBox.Show($"Uninstallation error:\n{ex.Message}", "Centrix",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
#else
                Console.Error.WriteLine($"Uninstallation error: {ex.Message}");
#endif
            }
        }
    }
}

#if NET8_0_WINDOWS
internal sealed class InstallForm : Form
{
    private string _root;
    private readonly bool _launch;
    private readonly TextBox _pathBox = new();
    private readonly Button _browseBtn = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _installBtn = new() { Text = "Install", AutoSize = true };
    private readonly Button _cancelBtn = new() { Text = "Cancel", AutoSize = true };
    private readonly Label _status = new()
    {
        Text = "Ready to install Centrix.",
        AutoSize = true,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Color.FromArgb(51, 41, 82),
        Margin = new Padding(0, 4, 0, 8)
    };
    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 25,
        Height = 8,
        Dock = DockStyle.Top,
        Visible = false
    };

    internal InstallForm(string root, bool launch)
    {
        _root = root;
        _launch = launch;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 270);
        Text = "Centrix Setup";
        BackColor = Color.FromArgb(20, 20, 19);

        try
        {
            var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LectureAgent.Setup.setup.ico");
            if (iconStream != null)
            {
                Icon = new Icon(iconStream);
            }
            else if (OperatingSystem.IsWindows() && File.Exists(Application.ExecutablePath))
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
        }
        catch
        {
            Icon = SystemIcons.Application;
        }

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 19),
            Padding = new Padding(18, 12, 18, 12)
        };

        if (Icon != null)
        {
            var logoBox = new PictureBox
            {
                Location = new Point(18, 12),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = Icon.ToBitmap()
            };
            header.Controls.Add(logoBox);
        }

        var title = new Label
        {
            Text = "Centrix",
            Font = new Font("Georgia", 14F, FontStyle.Regular),
            ForeColor = Color.FromArgb(236, 232, 225),
            AutoSize = true,
            Location = new Point(68, 10)
        };
        var subtitle = new Label
        {
            Text = "PhysicsWallah • Automatic Classroom Lecture Uploader",
            Font = new Font("Segoe UI", 8.25F),
            ForeColor = Color.FromArgb(142, 139, 133),
            AutoSize = true,
            Location = new Point(70, 36)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 19),
            Padding = new Padding(24, 16, 24, 12)
        };

        var pathLabel = new Label
        {
            Text = "Install Location (select any drive or folder):",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(236, 232, 225),
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 5)
        };

        _pathBox.Text = _root;
        _pathBox.Font = new Font("Segoe UI", 9.5F);
        _pathBox.BackColor = Color.FromArgb(28, 28, 26);
        _pathBox.ForeColor = Color.FromArgb(236, 232, 225);
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        _pathBox.Dock = DockStyle.Fill;

        _browseBtn.Font = new Font("Segoe UI", 9F);
        _browseBtn.BackColor = Color.FromArgb(35, 35, 32);
        _browseBtn.ForeColor = Color.FromArgb(236, 232, 225);
        _browseBtn.FlatStyle = FlatStyle.Flat;
        _browseBtn.FlatAppearance.BorderSize = 0;
        _browseBtn.Padding = new Padding(10, 3, 10, 3);
        _browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select the folder where Centrix should be installed:",
                ShowNewFolderButton = true
            };
            if (Directory.Exists(_pathBox.Text.Trim()))
            {
                dlg.SelectedPath = _pathBox.Text.Trim();
            }
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dlg.SelectedPath;
            }
        };

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 10)
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(_pathBox, 0, 0);
        pathRow.Controls.Add(_browseBtn, 1, 0);

        _status.Dock = DockStyle.Top;
        _status.ForeColor = Color.FromArgb(142, 139, 133);
        _progress.Dock = DockStyle.Top;

        body.Controls.Add(_progress);
        body.Controls.Add(_status);
        body.Controls.Add(pathRow);
        body.Controls.Add(pathLabel);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.FromArgb(28, 28, 26),
            Padding = new Padding(16, 8, 20, 8),
            Margin = Padding.Empty
        };

        _cancelBtn.Font = new Font("Segoe UI", 9F);
        _cancelBtn.BackColor = Color.FromArgb(35, 35, 32);
        _cancelBtn.ForeColor = Color.FromArgb(236, 232, 225);
        _cancelBtn.FlatStyle = FlatStyle.Flat;
        _cancelBtn.FlatAppearance.BorderSize = 0;
        _cancelBtn.Padding = new Padding(14, 5, 14, 5);
        _cancelBtn.Click += (_, _) => Close();

        _installBtn.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _installBtn.BackColor = Color.FromArgb(237, 234, 229);
        _installBtn.ForeColor = Color.FromArgb(20, 20, 19);
        _installBtn.FlatStyle = FlatStyle.Flat;
        _installBtn.FlatAppearance.BorderSize = 0;
        _installBtn.Padding = new Padding(20, 6, 20, 6);
        _installBtn.Click += async (_, _) =>
        {
            var chosen = _pathBox.Text.Trim().Trim('"', '\'').Trim();
            if (string.IsNullOrWhiteSpace(chosen))
            {
                MessageBox.Show(this, "Please specify a valid install folder.", "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _root = Path.GetFullPath(chosen);
            _installBtn.Enabled = false;
            _browseBtn.Enabled = false;
            _cancelBtn.Enabled = false;
            _pathBox.ReadOnly = true;
            _progress.Visible = true;
            _status.Text = "Extracting files and registering service…";

            var error = await Task.Run(() => Program.ExtractAndLaunch(_root, _launch));
            if (error != null)
            {
                _status.Text = "Installation encountered an issue.";
                _status.ForeColor = Color.Red;
                MessageBox.Show(this, error, "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _installBtn.Enabled = true;
                _browseBtn.Enabled = true;
                _cancelBtn.Enabled = true;
                _pathBox.ReadOnly = false;
                _progress.Visible = false;
                return;
            }

            Close();
        };

        btnPanel.Controls.Add(_cancelBtn);
        btnPanel.Controls.Add(_installBtn);

        AcceptButton = _installBtn;
        CancelButton = _cancelBtn;

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

        rootLayout.Controls.Add(header, 0, 0);
        rootLayout.Controls.Add(body, 0, 1);
        rootLayout.Controls.Add(btnPanel, 0, 2);

        Controls.Add(rootLayout);
    }
}
#endif
