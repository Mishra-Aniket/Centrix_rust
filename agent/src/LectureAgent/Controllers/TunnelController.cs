namespace LectureAgent.Presentation.Controllers;

using LectureAgent.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Endpoints for 1-Click Mobile Cloud Tunnel (Cloudflare Quick Tunnel).
/// Allows remote access from mobile phones / tablets on any network (4G/5G/Wi-Fi).
/// </summary>
[ApiController]
[Route("api/tunnel")]
public sealed class TunnelController : ControllerBase
{
    private readonly CloudTunnelService _tunnelService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TunnelController> _logger;

    public TunnelController(
        CloudTunnelService tunnelService,
        IConfiguration configuration,
        ILogger<TunnelController> logger)
    {
        _tunnelService = tunnelService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("status")]
    public ActionResult<TunnelStatusDto> GetStatus()
    {
        return Ok(_tunnelService.GetStatus());
    }

    [HttpPost("start")]
    public async Task<ActionResult<TunnelStatusDto>> StartTunnel(CancellationToken ct)
    {
        _logger.LogInformation("Request to start Cloudflare Quick Tunnel received.");
        var port = 5200;
        var status = await _tunnelService.StartTunnelAsync(port, ct);
        return Ok(status);
    }

    [HttpPost("stop")]
    public async Task<ActionResult<TunnelStatusDto>> StopTunnel()
    {
        _logger.LogInformation("Request to stop Cloudflare Quick Tunnel received.");
        var status = await _tunnelService.StopTunnelAsync();
        return Ok(status);
    }
}
