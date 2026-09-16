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
    private static void Main()
    {
        var root = Environment.GetEnvironmentVariable("LA_SETUP_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                InstallDirName);
        }

        var noLaunch = Environment.GetEnvironmentVariable("LA_SETUP_NO_LAUNCH") == "1";
        var noUi = Environment.GetEnvironmentVariable("LA_SETUP_NO_UI") == "1"
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
    internal static string? ExtractAndLaunch(string root, bool launch)
    {
        string? error = Extract(root);
        if (error != null)
        {
            return error;
        }

        var app = Path.Combine(root, "app", "Centrix.exe");
        if (!File.Exists(app))
        {
            app = Path.Combine(root, "app", "LectureAgentApp.exe");
        }

        CreateShortcuts(app);

        if (launch && File.Exists(app))
        {
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
        }

        return null;
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

            var desktopLnk = Path.Combine(desktopDir, "Centrix.lnk");
            var startMenuLnk = Path.Combine(programsDir, "Centrix.lnk");

            CreateShortcutPowerShell(desktopLnk, targetExe);
            CreateShortcutPowerShell(startMenuLnk, targetExe);
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
}

#if NET8_0_WINDOWS
internal sealed class InstallForm : Form
{
    private readonly string _root;
    private readonly bool _launch;
    private readonly Label _status = new()
    {
        Text = "Extracting program files and creating shortcuts…",
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F),
        ForeColor = Color.FromArgb(51, 41, 82),
        Margin = new Padding(0, 0, 0, 10)
    };
    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 25,
        Height = 8,
        Dock = DockStyle.Top
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
        ClientSize = new Size(480, 150);
        Text = "Centrix Setup";
        BackColor = Color.White;

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
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(27, 16, 51),
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
            Text = "Centrix Setup",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(68, 10)
        };
        var subtitle = new Label
        {
            Text = "PhysicsWallah • Automatic Classroom Lecture Uploader",
            Font = new Font("Segoe UI", 8.25F),
            ForeColor = Color.FromArgb(185, 174, 220),
            AutoSize = true,
            Location = new Point(70, 36)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 16)
        };
        body.Controls.Add(_progress);
        body.Controls.Add(_status);

        Controls.Add(body);
        Controls.Add(header);

        Shown += async (_, _) =>
        {
            var error = await Task.Run(() => Program.ExtractAndLaunch(_root, _launch));
            if (error != null)
            {
                MessageBox.Show(error, "Centrix Setup", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            Close();
        };
    }
}
#endif
