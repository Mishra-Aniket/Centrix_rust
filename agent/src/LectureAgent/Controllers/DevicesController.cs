using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace LectureAgent.Presentation.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly LectureContext _dbContext;

    public DevicesController(LectureContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<Device>>> List([FromQuery] string? centerId)
    {
        var query = _dbContext.Devices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(centerId))
            query = query.Where(device => device.CenterId == centerId);

        return Ok(await query.OrderBy(device => device.DeviceName).ToListAsync());
    }

    [HttpPost("register")]
    public async Task<ActionResult<Device>> Register([FromBody] RegisterDeviceRequest request)
    {
        var device = await _dbContext.Devices.FindAsync(request.DeviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = request.DeviceId,
                CreatedAt = DateTime.UtcNow
            };
            await _dbContext.Devices.AddAsync(device);
        }

        device.OrganizationId = request.OrganizationId;
        device.CenterId = request.CenterId;
        device.RoomId = request.RoomId;
        device.DeviceName = request.DeviceName;
        device.DeviceType = request.DeviceType;
        device.RecordingFolderPath = request.RecordingFolderPath;
        device.Status = DeviceStatus.Active;
        device.OnlineStatus = OnlineStatus.Online;
        device.LastHeartbeat = DateTime.UtcNow;
        device.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(device);
    }

    [HttpPost("{deviceId}/heartbeat")]
    public async Task<ActionResult<Device>> Heartbeat(string deviceId)
    {
        var device = await _dbContext.Devices.FindAsync(deviceId);
        if (device == null)
            return NotFound();

        device.OnlineStatus = OnlineStatus.Online;
        device.LastHeartbeat = DateTime.UtcNow;
        device.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return Ok(device);
    }
}

public sealed class RegisterDeviceRequest
{
    [Required] public string DeviceId { get; set; } = null!;
    [Required] public string OrganizationId { get; set; } = null!;
    [Required] public string CenterId { get; set; } = null!;
    [Required] public string RoomId { get; set; } = null!;
    [Required] public string DeviceName { get; set; } = null!;
    public string? DeviceType { get; set; }
    [Required] public string RecordingFolderPath { get; set; } = null!;
}
