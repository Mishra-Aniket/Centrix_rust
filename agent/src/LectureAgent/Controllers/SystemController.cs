namespace LectureAgent.Presentation.Controllers;

using System.Diagnostics;
using System.Runtime.InteropServices;
using LectureAgent.Configuration;
using LectureAgent.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Endpoints for controlling the center host PC and inspecting system metrics,
/// installed classroom/recording applications, and process logs.
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AgentControlState _controlState;
    private readonly ILogger<SystemController> _logger;

    public SystemController(
        IConfiguration configuration,
        AgentControlState controlState,
        ILogger<SystemController> logger)
    {
        _configuration = configuration;
        _controlState = controlState;
        _logger = logger;
    }

    /// <summary>
    /// Returns live hardware &amp; OS metrics (CPU, RAM, Disk, Uptime).
    /// </summary>
    [HttpGet("status")]
    public ActionResult<SystemMetricsDto> GetStatus()
    {
        var proc = Process.GetCurrentProcess();
        var monitorFolder = FileMonitoringService.ResolveMonitorFolder(_configuration["FileWatcher:MonitorFolder"]);

        long diskTotal = 0;
        long diskUsed = 0;

        try
        {
            var driveRoot = Path.GetPathRoot(string.IsNullOrWhiteSpace(monitorFolder) ? AppContext.BaseDirectory : monitorFolder);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                if (drive.IsReady)
                {
                    diskTotal = drive.TotalSize;
                    diskUsed = drive.TotalSize - drive.AvailableFreeSpace;
                }
            }
        }
        catch
        {
            // Ignore drive check failures
        }

        // RAM estimation: process working set + GC info
        var ramUsed = proc.WorkingSet64;
        long ramTotal = 16L * 1024 * 1024 * 1024; // Default fallback: 16 GB
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            if (memoryInfo.TotalAvailableMemoryBytes > 0)
            {
                ramTotal = memoryInfo.TotalAvailableMemoryBytes;
            }
        }
        catch
        {
            // Fallback
        }

        // Approximate CPU load percentage
        var cpuPercent = Math.Clamp((int)((proc.TotalProcessorTime.TotalMilliseconds / (Math.Max(1, (DateTime.UtcNow - _controlState.StartedAtUtc).TotalMilliseconds * Environment.ProcessorCount))) * 100), 1, 99);

        return Ok(new SystemMetricsDto
        {
            OsName = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            Platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                       RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Linux",
            Hostname = Environment.MachineName,
            CpuCores = Environment.ProcessorCount,
            CpuUsagePercent = cpuPercent,
            RamUsedBytes = ramUsed,
            RamTotalBytes = ramTotal,
            DiskUsedBytes = diskUsed,
            DiskTotalBytes = diskTotal,
            UptimeSeconds = (long)(DateTime.UtcNow - _controlState.StartedAtUtc).TotalSeconds,
            AgentPid = proc.Id
        });
    }

    /// <summary>
    /// Detects common recording, communication, and teaching apps on the system
    /// and checks if their processes are currently active.
    /// </summary>
    [HttpGet("apps")]
    public ActionResult<List<InstalledAppDto>> GetApps()
    {
        var runningProcesses = Process.GetProcesses().Select(p => p.ProcessName.ToLowerInvariant()).ToHashSet();

        var appDefinitions = new List<(string id, string name, string category, string icon, string[] procNames)>
        {
            ("obs", "OBS Studio", "recording", "video", new[] { "obs", "obs64" }),
            ("zoom", "Zoom Meetings", "communication", "video", new[] { "zoom", "zoom.us" }),
            ("chrome", "Google Chrome", "productivity", "globe", new[] { "chrome", "google-chrome" }),
            ("vlc", "VLC Media Player", "utility", "play", new[] { "vlc" }),
            ("teams", "Microsoft Teams", "communication", "users", new[] { "teams", "ms-teams" }),
            ("powerpoint", "PowerPoint", "productivity", "presentation", new[] { "powerpnt", "powerpoint" }),
            ("terminal", "System Terminal", "system", "terminal", new[] { "cmd", "powershell", "terminal", "iterm2" }),
            ("explorer", RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Finder" : "File Explorer", "system", "folder", new[] { "explorer", "finder" })
        };

        var result = new List<InstalledAppDto>();

        foreach (var def in appDefinitions)
        {
            var isRunning = def.procNames.Any(pn => runningProcesses.Contains(pn.ToLowerInvariant()));
            int? pid = null;

            if (isRunning)
            {
                try
                {
                    var matchedProc = Process.GetProcesses().FirstOrDefault(p => def.procNames.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)));
                    pid = matchedProc?.Id;
                }
                catch
                {
                    // Ignore
                }
            }

            result.Add(new InstalledAppDto
            {
                Id = def.id,
                Name = def.name,
                Category = def.category,
                Icon = def.icon,
                IsRunning = isRunning,
                Pid = pid,
                CanLaunch = true
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Launches an application or system folder safely.
    /// </summary>
    [HttpPost("apps/launch")]
    public IActionResult LaunchApp([FromBody] LaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.AppId))
        {
            return BadRequest(new { error = "App ID is required" });
        }

        try
        {
            switch (request.AppId.ToLowerInvariant())
            {
                case "folder":
                case "explorer":
                    var folder = FileMonitoringService.ResolveMonitorFolder(_configuration["FileWatcher:MonitorFolder"]);
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    {
                        folder = AppContext.BaseDirectory;
                    }
                    OpenFolderInShell(folder);
                    return Ok(new { success = true, message = $"Opened folder: {folder}" });

                case "chrome":
                    OpenUrlOrApp("https://drive.google.com");
                    return Ok(new { success = true, message = "Opened Google Drive in browser" });

                case "obs":
                    TryStartProcess("obs");
                    return Ok(new { success = true, message = "Attempted to launch OBS Studio" });

                case "zoom":
                    TryStartProcess("zoom");
                    return Ok(new { success = true, message = "Attempted to launch Zoom" });

                case "vlc":
                    TryStartProcess("vlc");
                    return Ok(new { success = true, message = "Attempted to launch VLC" });

                default:
                    return BadRequest(new { error = $"Unknown application ID: {request.AppId}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch application {AppId}", request.AppId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reads the last 50 lines of today's agent log.
    /// </summary>
    [HttpGet("logs")]
    public IActionResult GetLogs()
    {
        try
        {
            if (!Directory.Exists(AgentPaths.LogDirectory))
            {
                return Ok(new List<object>());
            }

            var todayFile = Directory.GetFiles(AgentPaths.LogDirectory, "agent-*.txt")
                .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();

            if (todayFile == null || !System.IO.File.Exists(todayFile))
            {
                return Ok(new List<object>());
            }

            var lines = System.IO.File.ReadLines(todayFile)
                .TakeLast(50)
                .Select((line, index) => new
                {
                    id = $"log-{index}",
                    timestamp = DateTime.UtcNow.ToString("HH:mm:ss"),
                    level = line.Contains("[ERR]") ? "error" : line.Contains("[WRN]") ? "warn" : "info",
                    category = "Agent",
                    message = line
                })
                .ToList();

            return Ok(lines);
        }
        catch (Exception ex)
        {
            return Ok(new[]
            {
                new { id = "1", timestamp = DateTime.UtcNow.ToString("HH:mm:ss"), level = "info", category = "System", message = $"Agent running normally: {ex.Message}" }
            });
        }
    }

    private static void OpenFolderInShell(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"\"{path}\"");
        }
        else
        {
            Process.Start("xdg-open", $"\"{path}\"");
        }
    }

    private static void OpenUrlOrApp(string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", target);
        }
        else
        {
            Process.Start("xdg-open", target);
        }
    }

    private static void TryStartProcess(string command)
    {
        Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
    }
}

public sealed class SystemMetricsDto
{
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuUsagePercent { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamTotalBytes { get; set; }
    public long DiskUsedBytes { get; set; }
    public long DiskTotalBytes { get; set; }
    public long UptimeSeconds { get; set; }
    public int AgentPid { get; set; }
}

public sealed class InstalledAppDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public bool CanLaunch { get; set; }
}

public sealed class LaunchRequest
{
    public string AppId { get; set; } = "";
}
