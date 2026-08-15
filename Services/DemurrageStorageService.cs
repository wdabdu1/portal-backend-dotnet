using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

public record TierBreakdownLine(string TierLabel, int Days, decimal Rate, decimal Cost);

public record DemurrageStorageResult(
    bool Applicable,
    DateOnly? AnchorDate,
    DateOnly? StorageEndDate, bool StorageEndIsActual,
    DateOnly? DemurrageEndDate, bool DemurrageEndIsActual,
    int StorageDays, int DemurrageDays,
    int Fcl20Count, int Fcl40Count,
    int StorageFreeDays, int StorageChargeableDays,
    int? DemurrageFreeDays20, int? DemurrageChargeableDays20,
    int? DemurrageFreeDays40, int? DemurrageChargeableDays40,
    decimal StorageCostEuro, decimal StorageCostSdg,
    decimal DemurrageCostSdg,
    decimal TotalStorageDemurrageSdg,
    List<string> Warnings,
    List<TierBreakdownLine> StorageBreakdown,
    List<TierBreakdownLine> DemurrageBreakdown20,
    List<TierBreakdownLine> DemurrageBreakdown40);

// Storage: SPC's charge for goods sitting in port, tiered by day count
// (universal tiers, same for everyone). Demurrage: the Shipping Line's
// charge for holding their container too long, tiered per Tariff Group
// (varies by goods type) and container size. Both start from the same
// anchor (DO Actual Arrival Date, falling back to ETA) and use the
// cascading SLA schedule's projected "Truck & Containers" date as the
// anticipated end until a real actual date is entered. Route 3 never
// applies — goods are already in the FZ, no vessel container to return.
public class DemurrageStorageService
{
    private readonly ShippingPortalDbContext _db;
    private readonly ClearanceScheduleService _scheduleService;

    public DemurrageStorageService(ShippingPortalDbContext db, ClearanceScheduleService scheduleService)
    {
        _db = db;
        _scheduleService = scheduleService;
    }

    public async Task<DemurrageStorageResult> CalculateAsync(int shipmentId, bool asOfToday = false)
    {
        var warnings = new List<string>();

        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null)
            return new DemurrageStorageResult(false, null, null, false, null, false, 0, 0, 0, 0, 0, 0, null, null, null, null, 0, 0, 0, 0, warnings, new(), new(), new());

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var route = clearance?.Route ?? ClearanceRouteType.NotSelected;

        if (route == ClearanceRouteType.NotSelected || route == ClearanceRouteType.Route3ClearFromFz)
            return new DemurrageStorageResult(false, null, null, false, null, false, 0, 0, shipment.Fcl20Count, shipment.Fcl40Count, 0, 0, null, null, null, null, 0, 0, 0, 0, warnings, new(), new(), new());

        var schedule = await _scheduleService.GetScheduleAsync(shipmentId);
        if (!schedule.AnchorDate.HasValue)
            return new DemurrageStorageResult(false, null, null, false, null, false, 0, 0, shipment.Fcl20Count, shipment.Fcl40Count, 0, 0, null, null, null, null, 0, 0, 0, 0, warnings, new(), new(), new());

        var anchor = schedule.AnchorDate.Value;
        var truckContainersItem = schedule.Items.FirstOrDefault(i => i.GroupItem == "Truck & Containers");
        // Normal mode projects to the SLA-projected completion date —
        // "what we'll owe if nothing else changes." asOfToday instead
        // stops the clock right now — the real, already-accrued
        // exposure, which is what actually matters for prioritizing
        // action today rather than forecasting a future bill.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var projectedEnd = asOfToday ? today : (truckContainersItem?.TargetDate ?? anchor);

        // Actual end dates come from the route-specific detail tables.
        DateOnly? truckPortEntryActual = null; // not currently a "final" marker on its own — storage end is the specific field below
        DateOnly? storageActualEnd = null;
        DateOnly? demurrageActualEnd = null;

        if (route == ClearanceRouteType.Route1ClearAtPort)
        {
            var r1 = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance!.Id);
            storageActualEnd = r1?.TruckPortEntryPermitDate;
            demurrageActualEnd = r1?.ContainersReturnedDate;
        }
        else if (route == ClearanceRouteType.Route2FzDeposit)
        {
            var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance!.Id);
            storageActualEnd = r2?.TruckPortEntryPermitDate;
            demurrageActualEnd = r2?.ContainersReturnedDate;
        }

        var storageEnd = storageActualEnd ?? projectedEnd;
        var demurrageEnd = demurrageActualEnd ?? projectedEnd;

        var storageDays = Math.Max(0, storageEnd.DayNumber - anchor.DayNumber);
        var demurrageDays = Math.Max(0, demurrageEnd.DayNumber - anchor.DayNumber);

        // --- Storage (SPC tiers, universal — free days come from Tarif-1's duration) ---
        var tiers = await _db.SpcStorageTiers.OrderBy(t => t.TierOrder).ToListAsync();
        var (storageEuro20, storageEuro40, storageBreakdown) = CalculateTieredEuro(storageDays, tiers, shipment.Fcl20Count, shipment.Fcl40Count);
        var storageEuro = storageEuro20 * shipment.Fcl20Count + storageEuro40 * shipment.Fcl40Count;

        var spcRate = await _db.SpcRates.OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        if (spcRate is null) warnings.Add("No SPC Euro Rate configured — storage cost shown in Euro only.");
        var storageSdg = storageEuro * (spcRate?.EuroToSdgRate ?? 0);

        var storageFreeDays = tiers.FirstOrDefault(t => t.TierOrder == 1)?.DurationDays ?? 0;
        var storageChargeableDays = Math.Max(0, storageDays - storageFreeDays);

        // --- Demurrage (Shipping Line tariff, per Tariff Group + container size) ---
        var firstLineItem = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .FirstOrDefaultAsync();

        var tariffGroupId = firstLineItem?.PurchaseOrderLineItem?.ProductCategory?.TariffGroupId;
        decimal demurrageSdg = 0;
        int? demFreeDays20 = null, demChargeableDays20 = null, demFreeDays40 = null, demChargeableDays40 = null;
        var demurrageBreakdown20 = new List<TierBreakdownLine>();
        var demurrageBreakdown40 = new List<TierBreakdownLine>();

        if (!shipment.ShippingLineId.Equals(default) && tariffGroupId.HasValue)
        {
            var tariff20 = await _db.ShippingLineDemurrageTariffs.FirstOrDefaultAsync(t =>
                t.ShippingLineId == shipment.ShippingLineId && t.TariffGroupId == tariffGroupId && t.ContainerSize == "20");
            var tariff40 = await _db.ShippingLineDemurrageTariffs.FirstOrDefaultAsync(t =>
                t.ShippingLineId == shipment.ShippingLineId && t.TariffGroupId == tariffGroupId && t.ContainerSize == "40");

            if (tariff20 is null && shipment.Fcl20Count > 0) warnings.Add("No demurrage tariff configured for this Shipping Line / Tariff Group / 20' combination.");
            if (tariff40 is null && shipment.Fcl40Count > 0) warnings.Add("No demurrage tariff configured for this Shipping Line / Tariff Group / 40' combination.");

            var (demurrageCost20, demBreakdown20) = CalculateDemurrageForSize(demurrageDays, tariff20, shipment.Fcl20Count);
            var (demurrageCost40, demBreakdown40) = CalculateDemurrageForSize(demurrageDays, tariff40, shipment.Fcl40Count);
            demurrageSdg += demurrageCost20 + demurrageCost40;
            demurrageBreakdown20 = demBreakdown20;
            demurrageBreakdown40 = demBreakdown40;

            if (tariff20 is not null) { demFreeDays20 = tariff20.FreeDays; demChargeableDays20 = Math.Max(0, demurrageDays - tariff20.FreeDays); }
            if (tariff40 is not null) { demFreeDays40 = tariff40.FreeDays; demChargeableDays40 = Math.Max(0, demurrageDays - tariff40.FreeDays); }
        }
        else
        {
            warnings.Add("No Tariff Group set on this shipment's product category — demurrage cannot be calculated.");
        }

        return new DemurrageStorageResult(
            true, anchor, storageEnd, storageActualEnd.HasValue, demurrageEnd, demurrageActualEnd.HasValue,
            storageDays, demurrageDays, shipment.Fcl20Count, shipment.Fcl40Count,
            storageFreeDays, storageChargeableDays,
            demFreeDays20, demChargeableDays20, demFreeDays40, demChargeableDays40,
            storageEuro, storageSdg, demurrageSdg, storageSdg + demurrageSdg, warnings,
            storageBreakdown, demurrageBreakdown20, demurrageBreakdown40);
    }

    // Breakdown combines both container sizes into one line per tier
    // (rate shown is the blended per-container cost for that tier,
    // matching how the free/chargeable summary is already presented).
    private static (decimal Rate20, decimal Rate40, List<TierBreakdownLine> Breakdown) CalculateTieredEuro(
        int totalDays, List<Models.Lookups.SpcStorageTier> tiers, int fcl20Count, int fcl40Count)
    {
        decimal cost20 = 0, cost40 = 0;
        var remaining = totalDays;
        var breakdown = new List<TierBreakdownLine>();

        foreach (var tier in tiers.OrderBy(t => t.TierOrder))
        {
            if (remaining <= 0) break;
            var tierDays = tier.DurationDays.HasValue ? Math.Min(remaining, tier.DurationDays.Value) : remaining;
            var tierCost20 = tierDays * tier.Rate20;
            var tierCost40 = tierDays * tier.Rate40;
            cost20 += tierCost20;
            cost40 += tierCost40;
            remaining -= tierDays;

            if (tierDays > 0)
            {
                var totalTierCost = (tierCost20 * fcl20Count) + (tierCost40 * fcl40Count);
                var label = tier.TierOrder == 1 ? $"Free period (Tier {tier.TierOrder})" : $"Tier {tier.TierOrder}";
                breakdown.Add(new TierBreakdownLine(label, tierDays, tier.Rate20, totalTierCost));
            }
        }

        return (cost20, cost40, breakdown);
    }

    private static (decimal Cost, List<TierBreakdownLine> Breakdown) CalculateDemurrageForSize(
        int totalDays, Models.Lookups.ShippingLineDemurrageTariff? tariff, int containerCount)
    {
        var breakdown = new List<TierBreakdownLine>();
        if (tariff is null || containerCount <= 0) return (0, breakdown);

        var chargeableDays = Math.Max(0, totalDays - tariff.FreeDays);
        var firstPeriodDaysCharged = Math.Min(chargeableDays, tariff.FirstPeriodDays);
        var afterwardDaysCharged = Math.Max(0, chargeableDays - tariff.FirstPeriodDays);

        var firstPeriodCost = firstPeriodDaysCharged * tariff.FirstPeriodRateSdg * containerCount;
        var afterwardCost = afterwardDaysCharged * tariff.AfterwardRateSdg * containerCount;

        if (tariff.FreeDays > 0) breakdown.Add(new TierBreakdownLine("Free days", tariff.FreeDays, 0, 0));
        if (firstPeriodDaysCharged > 0) breakdown.Add(new TierBreakdownLine("First period", firstPeriodDaysCharged, tariff.FirstPeriodRateSdg, firstPeriodCost));
        if (afterwardDaysCharged > 0) breakdown.Add(new TierBreakdownLine("Afterward", afterwardDaysCharged, tariff.AfterwardRateSdg, afterwardCost));

        var perContainer = firstPeriodCost + afterwardCost;
        return (perContainer, breakdown);
    }
}
