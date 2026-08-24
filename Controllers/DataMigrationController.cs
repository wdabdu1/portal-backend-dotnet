using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        var uploaderUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        using var stream = file.OpenReadStream();
        var summary = await service.ProcessAsync(stream, uploaderUserId);
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

    // Permanently deletes one PO and everything owned by it — for
    // correcting a mistaken or cancelled order. SuperUser-only, given
    // this is irreversible and touches real operational data.
    [HttpPost("delete-po")]
    [Authorize(Roles = AppRoles.SuperUser)]
    public async Task<IActionResult> DeletePo([FromBody] DeletePoRequest req, [FromServices] DeletePurchaseOrderService service)
    {
        var result = await service.DeleteAsync(req.PoNumber);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(new { message = result.Message });
    }

    // TEMPORARY — the TruckMovements table was never actually created by
    // its migration (same root cause as the CurrentCityId column earlier).
    // Remove this endpoint once run once.
    [HttpPost("fix-truck-movements-table")]
    [Authorize(Roles = AppRoles.SuperUser)]
    public async Task<IActionResult> FixTruckMovementsTable([FromServices] Data.ShippingPortalDbContext db)
    {
        var results = new List<string>();
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE `TruckMovements` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `TruckId` int NOT NULL,
                    `FromCityId` int NULL,
                    `ToCityId` int NOT NULL,
                    `MoveDate` date NOT NULL,
                    `Reason` longtext NULL,
                    `Value` decimal(65,30) NULL,
                    `Notes` longtext NULL,
                    `CreatedByUserId` longtext NOT NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    CONSTRAINT `PK_TruckMovements` PRIMARY KEY (`Id`)
                ) CHARACTER SET=utf8mb4;");
            results.Add("Created TruckMovements table.");
        }
        catch (Exception ex) { results.Add($"Create table skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX `IX_TruckMovements_TruckId` ON `TruckMovements` (`TruckId`)");
            results.Add("Added TruckId index.");
        }
        catch (Exception ex) { results.Add($"TruckId index skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX `IX_TruckMovements_FromCityId` ON `TruckMovements` (`FromCityId`)");
            results.Add("Added FromCityId index.");
        }
        catch (Exception ex) { results.Add($"FromCityId index skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX `IX_TruckMovements_ToCityId` ON `TruckMovements` (`ToCityId`)");
            results.Add("Added ToCityId index.");
        }
        catch (Exception ex) { results.Add($"ToCityId index skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `TruckMovements` ADD CONSTRAINT `FK_TruckMovements_Trucks_TruckId` FOREIGN KEY (`TruckId`) REFERENCES `Trucks` (`Id`) ON DELETE RESTRICT");
            results.Add("Added Truck FK.");
        }
        catch (Exception ex) { results.Add($"Truck FK skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `TruckMovements` ADD CONSTRAINT `FK_TruckMovements_LogisticsCities_FromCityId` FOREIGN KEY (`FromCityId`) REFERENCES `LogisticsCities` (`Id`) ON DELETE RESTRICT");
            results.Add("Added FromCity FK.");
        }
        catch (Exception ex) { results.Add($"FromCity FK skipped/failed: {ex.Message}"); }

        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `TruckMovements` ADD CONSTRAINT `FK_TruckMovements_LogisticsCities_ToCityId` FOREIGN KEY (`ToCityId`) REFERENCES `LogisticsCities` (`Id`) ON DELETE RESTRICT");
            results.Add("Added ToCity FK.");
        }
        catch (Exception ex) { results.Add($"ToCity FK skipped/failed: {ex.Message}"); }

        return Ok(new { results });
    }
}

public record DeletePoRequest(string PoNumber);
