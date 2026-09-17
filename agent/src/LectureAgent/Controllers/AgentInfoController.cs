namespace LectureAgent.Presentation.Controllers;

using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using LectureAgent.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Read-only agent information used by the dashboard login and settings screens,
/// including the LAN URLs a phone on the same WiFi should use to reach this agent.
/// </summary>
[ApiController]
[Route("api/agent")]
public sealed class AgentInfoController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AgentControlState _controlState;

    public AgentInfoController(IConfiguration configuration, AgentControlState controlState)
    {
        _configuration = configuration;
        _controlState = controlState;
    }

    [HttpGet("info")]
    public ActionResult<AgentInfoDto> GetInfo()
    {
        var monitorFolder = FileMonitoringService.ResolveMonitorFolder(_configuration["FileWatcher:MonitorFolder"]);

        return Ok(new AgentInfoDto
        {
            AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            OrganizationId = _configuration["Agent:OrganizationId"] ?? "",
            CenterId = _configuration["Agent:CenterId"] ?? "",
            RoomId = _configuration["Agent:RoomId"] ?? "",
            DeviceId = _configuration["Agent:DeviceId"] ?? "",
            MachineName = Environment.MachineName,
            MonitorFolder = monitorFolder,
            NotesFolder = FileMonitoringService.ResolveOptionalFolder(_configuration["FileWatcher:NotesFolder"]) ?? "",
            FileWatcherEnabled = bool.TryParse(_configuration["FileWatcher:EnableFileWatcher"], out var watcherEnabled) && watcherEnabled,
            GoogleDriveEnabled = bool.TryParse(_configuration["GoogleDrive:Enabled"], out var driveEnabled) && driveEnabled,
            DriveRootFolder = _configuration["GoogleDrive:RootFolderPath"] ?? "",
            QueueUnmatchedFiles = bool.TryParse(_configuration["UploadQueue:QueueUnmatchedFiles"], out var queueUnmatched) && queueUnmatched,
            SheetSyncIntervalMinutes = _configuration.GetValue("GoogleSheet:SyncIntervalMinutes", 5),
            LanIpv4Addresses = GetLanIpv4Addresses(),
            HttpPort = 5200,
            HttpsPort = 5201,
            StartedAtUtc = _controlState.StartedAtUtc,
            UptimeSeconds = (long)(DateTime.UtcNow - _controlState.StartedAtUtc).TotalSeconds,
            UploadsPaused = _controlState.UploadsPaused,
            MonitoringPaused = _controlState.MonitoringPaused,
            TimetableSyncPaused = _controlState.TimetableSyncPaused,
            YouTubeEnabled = bool.TryParse(_configuration["YouTube:Enabled"], out var ytEnabled) && ytEnabled
        });
    }

    private static List<string> GetLanIpv4Addresses()
    {
        var addresses = new List<string>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Include virtual adapters too (Tailscale 100.x.x.x, etc.) so the
                // dashboard can show every address the agent is reachable on.
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !address.Address.ToString().StartsWith("127.")
                        && !address.Address.ToString().StartsWith("169.254.")
                        && !addresses.Contains(address.Address.ToString()))
                    {
                        addresses.Add(address.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // Network enumeration is best-effort; an empty list just hides the URLs in the UI.
        }

        return addresses;
    }
}

/// <summary>
/// Agent identity, configuration and runtime state for the dashboard.
/// </summary>
public sealed class AgentInfoDto
{
    public string AgentVersion { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string CenterId { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string MonitorFolder { get; set; } = "";
    public string NotesFolder { get; set; } = "";
    public bool FileWatcherEnabled { get; set; }
    public bool GoogleDriveEnabled { get; set; }
    public string DriveRootFolder { get; set; } = "";
    public bool QueueUnmatchedFiles { get; set; }
    public int SheetSyncIntervalMinutes { get; set; }
    public List<string> LanIpv4Addresses { get; set; } = new();
    public int HttpPort { get; set; }
    public int HttpsPort { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public long UptimeSeconds { get; set; }
    public bool UploadsPaused { get; set; }
    public bool MonitoringPaused { get; set; }
    public bool TimetableSyncPaused { get; set; }
    public bool YouTubeEnabled { get; set; }
}
