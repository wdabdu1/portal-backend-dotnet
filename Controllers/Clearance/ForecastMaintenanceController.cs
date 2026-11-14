using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

public record ForecastRecalcRow(string BlAwbNo, decimal? OldDemurrageSdg, decimal? NewDemurrageSdg, decimal? OldStorageSdg, decimal? NewStorageSdg);
public record ForecastRecalcResult(int Updated, int Unchanged, int SkippedLocked, int NotApplicable, List<ForecastRecalcRow> Changes);

// One-click admin action: re-computes the SLA-plan forecast (see ClearanceScheduleService.GetSlaPlanEndDateAsync)
// for every shipment that already has a forecast captured - e.g. after the forecast definition changed, or
// after tariffs / SLA targets were corrected. Actual Paid figures are never touched. Shipments whose
// Actual Charges block is locked keep their frozen forecast.
[ApiController]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
[Route("api/clearance/recalculate-all-forecasts")]
public class ForecastMaintenanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly DemurrageStorageService _demurrage;
    private readonly ClearanceScheduleService _schedule;

    public ForecastMaintenanceController(ShippingPortalDbContext db, DemurrageStorageService demurrage, ClearanceScheduleService schedule)
    { _db = db; _demurrage = demurrage; _schedule = schedule; }

    [HttpPost]
    public async Task<ActionResult<ForecastRecalcResult>> Run()
    {
        var rows = await _db.ClearanceActualCharges
            .Where(c => c.ForecastCapturedAt != null)
            .Select(c => new { Charges = c, c.Clearance!.ShipmentId, Bl = c.Clearance.Shipment!.BlAwbNo })
            .ToListAsync();

        var lockedShipmentIds = (await _db.SectionLocks
            .Where(l => l.EntityType == "Clearance" &&
                        (l.SectionKey == "route1ActualCharges" || l.SectionKey == "route2ActualCharges" || l.SectionKey == "actualCharges"))
            .Select(l => l.EntityId).ToListAsync()).ToHashSet();

        int updated = 0, unchanged = 0, skipped = 0, na = 0;
        var changes = new List<ForecastRecalcRow>();

        foreach (var r in rows)
        {
            if (lockedShipmentIds.Contains(r.ShipmentId)) { skipped++; continue; }

            var planEnd = await _schedule.GetSlaPlanEndDateAsync(r.ShipmentId);
            if (!planEnd.HasValue) { na++; continue; }
            var calc = await _demurrage.CalculateAsync(r.ShipmentId, false, planEnd);
            if (!calc.Applicable) { na++; continue; }

            var c = r.Charges;
            var same = c.ForecastDemurrageSdg == calc.DemurrageCostSdg && c.ForecastStorageSdg == calc.StorageCostSdg && c.PlannedCompletionDate == planEnd;
            if (same) { unchanged++; continue; }

            if (c.ForecastDemurrageSdg != calc.DemurrageCostSdg || c.ForecastStorageSdg != calc.StorageCostSdg)
                changes.Add(new ForecastRecalcRow(r.Bl, c.ForecastDemurrageSdg, calc.DemurrageCostSdg, c.ForecastStorageSdg, calc.StorageCostSdg));

            c.PlannedCompletionDate = planEnd;
            c.ForecastDemurrageSdg = calc.DemurrageCostSdg;
            c.ForecastStorageSdg = calc.StorageCostSdg;
            c.ForecastCapturedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0) await _db.SaveChangesAsync();
        return new ForecastRecalcResult(updated, unchanged, skipped, na, changes.Take(200).ToList());
    }
}
