using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Settings;

public record MotCertificateSettingsUpdateRequest(int ExpiryDays);

[ApiController]
[Authorize]
[Route("api/settings/mot-certificate-settings")]
public class MotCertificateSettingsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public MotCertificateSettingsController(ShippingPortalDbContext db) => _db = db;

    // Always exactly one row (seeded by mot-certificate-migration.sql) —
    // get-or-create defensively in case an environment never ran the seed
    // insert, same idiom as LogisticsVisibilitySettingsController.
    private async Task<MotCertificateSettings> GetOrCreateAsync()
    {
        var settings = await _db.MotCertificateSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new MotCertificateSettings();
            _db.MotCertificateSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    [HttpGet]
    public async Task<ActionResult<MotCertificateSettings>> Get() => await GetOrCreateAsync();

    [HttpPut]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<MotCertificateSettings>> Update(MotCertificateSettingsUpdateRequest req)
    {
        if (req.ExpiryDays <= 0) return BadRequest(new { message = "Expiry days must be greater than zero." });

        var settings = await GetOrCreateAsync();
        settings.ExpiryDays = req.ExpiryDays;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return settings;
    }
}
