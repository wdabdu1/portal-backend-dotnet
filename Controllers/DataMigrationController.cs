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
    [HttpGet("settings-export")]
    public async Task<IActionResult> ExportSettings([FromServices] SettingsExportService service)
    {
        var bytes = await service.ExportAsync();
        var fileName = $"CTC_Portal_Settings_Backup_{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public record CompleteDeleteRequest(string ConfirmationPhrase);

    // Deliberately requires typing an exact phrase, not just a role
    // check — this wipes every Settings and operational data table in
    // one action, and that shouldn't be one click away for anyone.
    [HttpPost("complete-delete")]
    public async Task<IActionResult> CompleteDelete(CompleteDeleteRequest req, [FromServices] CompleteDeleteService service)
    {
        if (req.ConfirmationPhrase != "DELETE EVERYTHING")
            return BadRequest(new { message = "Confirmation phrase did not match. Type exactly: DELETE EVERYTHING" });

        var wipedTables = await service.DeleteAllAsync();
        return Ok(new { message = $"Wiped {wipedTables.Count} tables.", tables = wipedTables });
    }

    [HttpPost("settings-upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<UploadSummary>> UploadSettings(IFormFile file, [FromServices] SettingsUploadService service)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        using var stream = file.OpenReadStream();
        var summary = await service.ProcessAsync(stream);
        return Ok(summary);
    }

    [HttpPost("data-upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<UploadSummary>> UploadData(IFormFile file, [FromServices] DataUploadService service)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        using var stream = file.OpenReadStream();
        var summary = await service.ProcessAsync(stream);
        return Ok(summary);
    }

    [HttpGet("data-export")]
    public async Task<IActionResult> ExportData([FromServices] DataExportService service)
    {
        var bytes = await service.ExportAsync();
        var fileName = $"CTC_Portal_Data_Backup_{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // One-time, deliberately not wired into any frontend button — the
    // scenario data (BU/Division/Supplier families) is hand-built for
    // this specific Settings baseline. Run directly via API once.
    [HttpPost("generate-test-data")]
    public async Task<IActionResult> GenerateTestData([FromServices] TestDataGeneratorService service)
    {
        var summary = await service.GenerateAsync();
        return Ok(new { message = summary });
    }
}
