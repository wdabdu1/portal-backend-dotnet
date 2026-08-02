using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

public record DemurrageStorageResult(
    bool Applicable, DateOnly? StartDate,
    DateOnly? StorageEndDate, bool StorageIsActual, decimal StorageChargeSdg,
    DateOnly? DemurrageEndDate, bool DemurrageIsActual, decimal DemurrageChargeSdg,
    decimal TotalChargeSdg);

// Storage (SPC) and Demurrage (Shipping Line) are both zero for Route 3 —
// goods are already inside the Free Zone, no vessel container involved.
// Both start from the DO's Actual Arrival Date (falling back to Shipment
// ETA), and each ends at its own real-world milestone: Storage at Truck
// Port Entry Permit Date, Demurrage at Containers Returned Date — using the
// SLA-projected target date for whichever hasn't actually happened yet.
public class DemurrageStorageService
{
    private readonly ShippingPortalDbContext _db;
    private readonly ClearanceScheduleService _scheduleService;

    public DemurrageStorageService(ShippingPortalDbContext db, ClearanceScheduleService scheduleService)
    {
        _db = db;
        _scheduleService = scheduleService;
    }

    public async Task<DemurrageStorageResult> CalculateAsync(int shipmentId)
    {
        var shipment = await _db.Shipments
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return new DemurrageStorageResult(false, null, null, false, 0, null, false, 0, 0);

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null || clearance.Route == ClearanceRouteType.NotSelected || clearance.Route == ClearanceRouteType.Route3ClearFromFz)
            return new DemurrageStorageResult(false, null, null, false, 0, null, false, 0, 0);

        var schedule = await _scheduleService.GetScheduleAsync(shipmentId);
        if (!schedule.AnchorDate.HasValue)
            return new DemurrageStorageResult(false, null, null, false, 0, null, false, 0, 0);

        var startDate = schedule.AnchorDate.Value;

        var truckContainersItem = schedule.Items.FirstOrDefault(i => i.GroupItem == "Truck & Containers");
        var storageEndDate = truckContainersItem?.TargetDate;
        var storageIsActual = false;

        DateOnly? demurrageEndDate = null;
        var demurrageIsActual = false;

        if (clearance.Route == ClearanceRouteType.Route1ClearAtPort)
        {
            var r1 = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            if (r1?.TruckPortEntryPermitDate.HasValue == true) { storageEndDate = r1.TruckPortEntryPermitDate; storageIsActual = true; }
            demurrageEndDate = r1?.ContainersReturnedDate ?? truckContainersItem?.TargetDate;
            demurrageIsActual = r1?.ContainersReturnedDate.HasValue == true;
        }
        else if (clearance.Route == ClearanceRouteType.Route2FzDeposit)
        {
            var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            if (r2?.TruckPortEntryPermitDate.HasValue == true) { storageEndDate = r2.TruckPortEntryPermitDate; storageIsActual = true; }
            demurrageEndDate = r2?.ContainersReturnedDate ?? truckContainersItem?.TargetDate;
            demurrageIsActual = r2?.ContainersReturnedDate.HasValue == true;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var storageDays = storageEndDate.HasValue ? Math.Max(0, storageEndDate.Value.DayNumber - startDate.DayNumber) : 0;
        var demurrageDays = demurrageEndDate.HasValue ? Math.Max(0, demurrageEndDate.Value.DayNumber - startDate.DayNumber) : 0;

        var storageCharge = await CalculateStorageChargeAsync(storageDays, shipment.Fcl20Count, shipment.Fcl40Count);
        var demurrageCharge = await CalculateDemurrageChargeAsync(demurrageDays, shipment);

        return new DemurrageStorageResult(
            true, startDate,
            storageEndDate, storageIsActual, storageCharge,
            demurrageEndDate, demurrageIsActual, demurrageCharge,
            storageCharge + demurrageCharge);
    }

    private async Task<decimal> CalculateStorageChargeAsync(int totalDays, int fcl20, int fcl40)
    {
        if (totalDays <= 0 || (fcl20 == 0 && fcl40 == 0)) return 0;

        var tiers = await _db.SpcStorageTiers.OrderBy(t => t.TierOrder).ToListAsync();
        var spcRate = await _db.SpcRates.OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var euroToSdg = spcRate?.EuroToSdgRate ?? 0m;

        decimal totalEuro = 0;
        var remainingDays = totalDays;

        foreach (var tier in tiers)
        {
            if (remainingDays <= 0) break;
            var daysInTier = tier.DurationDays.HasValue ? Math.Min(remainingDays, tier.DurationDays.Value) : remainingDays;
            totalEuro += daysInTier * (fcl20 * tier.Rate20 + fcl40 * tier.Rate40);
            remainingDays -= daysInTier;
        }

        return totalEuro * euroToSdg;
    }

    private async Task<decimal> CalculateDemurrageChargeAsync(int totalDays, Models.Shipments.Shipment shipment)
    {
        if (totalDays <= 0 || shipment.ShippingLineId == 0) return 0;
        if (shipment.Fcl20Count == 0 && shipment.Fcl40Count == 0) return 0;

        var tariffGroupId = shipment.LineItems
            .Select(li => li.PurchaseOrderLineItem?.ProductCategory?.TariffGroupId)
            .FirstOrDefault(g
