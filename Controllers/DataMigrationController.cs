using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

// SuperUser-only — this is where "Settings Upload", "Data Upload",
// "Backup/Export", and "Complete Delete" all live, matching the plan
// to keep these behind restricted access, likely surfaced from the
// Users page rather than a menu everyone can see.
[ApiController]
[Route("api/data-migration")]
[Authorize(Roles = AppRoles.SuperUser)]
public class DataMigrationController : ControllerBase
{
    [HttpPost("settings-upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<UploadSummary>> UploadSettings(IFormFile file, [FromServices] SettingsUploadService service)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        using var stream = file.OpenReadStream();
        var summary = await service.ProcessAsync(stream);
        return Ok(summary);
    }
}
