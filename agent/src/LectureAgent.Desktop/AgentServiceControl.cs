using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;

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
    internal const string ServiceName = "Centrix";
    internal const string LegacyServiceName = "LectureAgent";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Win32 ERROR_CANCELLED — returned by Process.Start when the user declines the UAC prompt.</summary>
    private const int UacDeclinedErrorCode = 1223;

    internal static AgentServiceState GetState()
    {
        // Check current Centrix service first
        var state = CheckServiceState(ServiceName);
        if (state != AgentServiceState.NotInstalled)
        {
            return state;
        }

        // Fallback check for legacy LectureAgent service
        return CheckServiceState(LegacyServiceName);
    }

    private static AgentServiceState CheckServiceState(string name)
    {
        try
        {
            using var service = new ServiceController(name);
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
        var targetService = ServiceExists(ServiceName) ? ServiceName : LegacyServiceName;
        try
        {
            using var service = new ServiceController(targetService);
            if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StateTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or System.TimeoutException)
        {
            // The non-elevated attempt above can fail for two very different reasons:
            // the caller lacks rights to start the service, or the service process is
            // crashing immediately after Windows starts it. Retrying elevated only
            // fixes the first case, so EnsureRunning below verifies the real outcome
            // instead of assuming the elevated retry succeeded.
            RunElevated("sc.exe", $"start {targetService}");
            EnsureRunning(targetService);
        }
    }

    internal static void Restart()
    {
        var targetService = ServiceExists(ServiceName) ? ServiceName : LegacyServiceName;
        try
        {
            using var service = new ServiceController(targetService);
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
            RunElevated("cmd.exe", $"/c net stop {targetService} & net start {targetService}");
            EnsureRunning(targetService);
        }
    }

    /// <summary>
    /// Confirms the service is actually running (and stays running for a moment,
    /// rather than crashing right after Windows reports it as started) before
    /// Start()/Restart() are allowed to return successfully. Without this check, a
    /// service that crash-loops on launch (e.g. because another process already
    /// holds the dashboard port) silently looked like a no-op to the user instead
    /// of surfacing an actionable error.
    /// </summary>
    private static void EnsureRunning(string serviceName)
    {
        var deadline = DateTime.UtcNow + StateTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (CheckServiceState(serviceName) == AgentServiceState.Running)
            {
                // It can briefly report Running just before a startup crash, so
                // double-check a moment later before trusting it.
                Thread.Sleep(1500);
                if (CheckServiceState(serviceName) == AgentServiceState.Running)
                {
                    return;
                }

                continue;
            }

            Thread.Sleep(500);
        }

        int port;
        try
        {
            port = AgentSettings.Load().Port;
        }
        catch
        {
            port = 5200;
        }

        throw new InvalidOperationException(
            "The Centrix service did not stay running after being started. It is most likely crashing "
            + $"immediately on launch (a common cause is another process already using port {port}). "
            + $"Open the newest log file in \"{AppPaths.LogDirectory}\" for the exact error.");
    }

    private static bool ServiceExists(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            _ = service.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the service (auto-start), lets signed-in users start/stop it without admin
    /// rights, opens the dashboard port in Windows Firewall and starts it. Everything runs
    /// in one elevated script so the operator sees a single UAC prompt.
    /// </summary>
    internal static void Install(int dashboardPort)
    {
        var binPath = AgentExecutablePath;
        var script = $"""
            chcp 437 >nul
            net stop {LegacyServiceName} >nul 2>&1
            sc delete {LegacyServiceName} >nul 2>&1
            net stop {ServiceName} >nul 2>&1
            sc delete {ServiceName} >nul 2>&1
            sc create {ServiceName} binpath= "\"{binPath}\"" start= auto
            sc description {ServiceName} "Centrix - Automatic Classroom Lecture Uploader"
            sc failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/60000
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

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("cmd.exe could not be started.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UacDeclinedErrorCode)
            {
                throw new InvalidOperationException("Administrator approval was declined, so nothing was installed.", ex);
            }

            using (process)
            {
                process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds);
            }

            if (GetState() == AgentServiceState.NotInstalled)
            {
                throw new InvalidOperationException(
                    "Windows did not register the service. An administrator may need to run the setup once.");
            }

            EnsureRunning(ServiceName);
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

    /// <summary>
    /// Launches an elevated helper command and waits for it to finish. The actual outcome of
    /// service start/stop/install operations is verified separately by the caller (via
    /// <see cref="EnsureRunning"/> or a state check) because sc.exe/net.exe exit codes are not
    /// reliable indicators of whether the service is actually healthy afterwards.
    /// </summary>
    private static void RunElevated(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"'{fileName}' could not be started.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacDeclinedErrorCode)
        {
            throw new InvalidOperationException("Administrator approval was declined.", ex);
        }

        using (process)
        {
            if (!process.WaitForExit((int)StateTimeout.TotalMilliseconds))
            {
                throw new InvalidOperationException($"'{fileName} {arguments}' did not finish in time.");
            }
        }
    }
}
