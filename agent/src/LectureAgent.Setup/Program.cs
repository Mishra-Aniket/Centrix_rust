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
    internal const string InstallDirName = "LectureAgentApp";

    [STAThread]
    private static void Main()
    {
        // Test hooks: LA_SETUP_ROOT overrides the install folder, LA_SETUP_NO_LAUNCH
        // skips starting the desktop app, LA_SETUP_NO_UI skips the window entirely.
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
            Application.Run(new InstallForm(root, noLaunch));
            return;
        }
#endif

        var error = ExtractAndLaunch(root, noLaunch);
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

        if (launch)
        {
            var app = Path.Combine(root, "app", "LectureAgentApp.exe");
            if (File.Exists(app))
            {
                Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            }
        }

        return null;
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
            // The service keeps LectureAgent.exe open, so overwriting it fails while the
            // agent is running. Tell the user how to release the file.
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
        Text = "Installing Centrix, please wait…",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11f),
    };

    internal InstallForm(string root, bool launch)
    {
        _root = root;
        _launch = launch;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 90);
        Text = "Centrix Setup";
        Controls.Add(_status);
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
