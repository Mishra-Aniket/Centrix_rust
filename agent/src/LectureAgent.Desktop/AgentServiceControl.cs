using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace LectureAgent.Desktop;

internal enum AgentServiceState
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// Controls the LectureAgent Windows service. The installer grants interactive users
/// start/stop rights, so the common case needs no elevation; if the rights are missing the
/// call is retried through an elevated sc.exe, which raises the normal UAC prompt.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AgentServiceControl
{
    internal const string ServiceName = "LectureAgent";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    internal static AgentServiceState GetState()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            return service.Status switch
            {
                ServiceControllerStatus.Running => AgentServiceState.Running,
                ServiceControllerStatus.StartPending => AgentServiceState.Starting,
                ServiceControllerStatus.ContinuePending => AgentServiceState.Starting,
                ServiceControllerStatus.StopPending => AgentServiceState.Stopping,
                _ => AgentServiceState.Stopped
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return AgentServiceState.NotInstalled;
        }
    }

    internal static void Start()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StateTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.TimeoutException)
        {
            RunElevated("sc.exe", $"start {ServiceName}");
        }
    }

    internal static void Restart()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, StateTimeout);
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StateTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.TimeoutException)
        {
            // net stop blocks until the service has really stopped, so one elevated
            // command (one UAC prompt) is enough for the whole restart.
            RunElevated("cmd.exe", $"/c net stop {ServiceName} & net start {ServiceName}");
        }
    }

    /// <summary>
    /// Creates the service (auto-start), lets signed-in users start/stop it without admin
    /// rights, opens the dashboard port in Windows Firewall and starts it. Everything runs
    /// in one elevated script so the operator sees a single UAC prompt.
    /// </summary>
    internal static void Install(int dashboardPort)
    {
        var script = $"""
            chcp 437 >nul
            sc create {ServiceName} binpath= "{AgentExecutablePath}" start= auto
            sc description {ServiceName} "Watches the recordings folder and uploads lectures to Google Drive."
            sc sdset {ServiceName} D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;RPWPDTLO;;;IU)
            netsh advfirewall firewall delete rule name="Centrix Dashboard" >nul 2>&1
            netsh advfirewall firewall add rule name="Centrix Dashboard" dir=in action=allow protocol=TCP localport={dashboardPort}
            sc start {ServiceName}
            """;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"lecture-agent-install-{Environment.ProcessId}.cmd");
        File.WriteAllText(scriptPath, script);
        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("cmd.exe could not be started.");
            process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds);

            if (GetState() == AgentServiceState.NotInstalled)
            {
                throw new InvalidOperationException(
                    "Windows did not register the service. An administrator may need to run the setup once.");
            }
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch (IOException)
            {
                // A leftover temp script is harmless.
            }
        }
    }

    private static string AgentExecutablePath
    {
        get
        {
            if (File.Exists(AppPaths.AgentExecutable))
            {
                return AppPaths.AgentExecutable;
            }

            throw new InvalidOperationException(
                $"The agent program is missing at {AppPaths.AgentExecutable}. Reinstall Centrix.");
        }
    }

    private static void RunElevated(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit((int)StateTimeout.TotalMilliseconds);
    }
}
