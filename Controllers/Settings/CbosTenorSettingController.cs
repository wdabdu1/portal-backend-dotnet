using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

public record CbosTenorSettingResponse(int? TenorId, int? TenorDays);
public record SaveCbosTenorSettingRequest(int? TenorId);

// A single, system-wide row — not a lookup list. Corp Finance picks one
// Tenor here (from the same Tenor list Shipments/Banking already uses)
// to represent "the current CBOS Tenor"; Bank Dues/Pay Bank Dues read
// this live every time they compute a CBOS Due Date. Deliberately not
// built on LookupCrudController<T> — there's exactly one row, nothing
// to list/delete, and no per-row "in use" concern like a real lookup.
[ApiController]
[Route("api/settings/cbos-tenor")]
[Authorize]
public class CbosTenorSettingController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public CbosTenorSettingController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<CbosTenorSettingResponse>> Get()
    {
        var setting = await _db.CbosTenorSettings.Include(s => s.Tenor).FirstOrDefaultAsync();
        return Ok(new CbosTenorSettingResponse(setting?.TenorId, setting?.Tenor?.Days));
    }

    [HttpPut]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<CbosTenorSettingResponse>> Save(SaveCbosTenorSettingRequest req)
    {
        var setting = await _db.CbosTenorSettings.FirstOrDefaultAsync();
        if (setting is null)
        {
            setting = new CbosTenorSetting();
            _db.CbosTenorSettings.Add(setting);
        }

        setting.TenorId = req.TenorId;
        await _db.SaveChangesAsync();

        var tenorDays = req.TenorId.HasValue
            ? await _db.Tenors.Where(t => t.Id == req.TenorId).Select(t => (int?)t.Days).FirstOrDefaultAsync()
            : null;
        return Ok(new CbosTenorSettingResponse(setting.TenorId, tenorDays));
    }
}
