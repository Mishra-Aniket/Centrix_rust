using LectureAgent.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace LectureAgent.Controllers;

/// <summary>
/// Exposes the real Google Drive folder names so the dashboard's batch picker can
/// offer folders that actually exist instead of free-text guesses.
/// </summary>
[ApiController]
[Route("api/drive")]
public class DriveController : ControllerBase
{
    private readonly IGoogleDriveUploader _driveUploader;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DriveController> _logger;

    public DriveController(
        IGoogleDriveUploader driveUploader,
        IConfiguration configuration,
        ILogger<DriveController> logger)
    {
        _driveUploader = driveUploader;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists Drive folders, optionally filtered by a name fragment. When
    /// <paramref name="under"/> is "root", only the batch folders inside the configured
    /// root folder (GoogleDrive:RootFolderPath) are returned - not every folder on Drive.
    /// </summary>
    [HttpGet("folders")]
    public async Task<ActionResult<object>> GetFolders(
        [FromQuery] string? q, [FromQuery] string? under, [FromQuery] int limit = 200)
    {
        try
        {
            var underPath = string.Equals(under, "root", StringComparison.OrdinalIgnoreCase)
                ? _configuration["GoogleDrive:RootFolderPath"]
                : null;
            var folders = await _driveUploader.ListFoldersAsync(q, limit, underPath);
            return Ok(new { count = folders.Count, items = folders });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error listing Drive folders: {ex.Message}");
            return BadRequest(new { error = $"Could not list Google Drive folders: {ex.Message}" });
        }
    }
}
