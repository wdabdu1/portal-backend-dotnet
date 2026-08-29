using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers;

public record DemurrageHitDetail(
    string BlAwbNo, string BusinessUnit, decimal DemurrageStorageUsd, decimal ShipmentValueUsd, decimal MagnitudePercent);

// Days of Inventory: average age (days since goods physically arrived
// at that FZ) across deposits with zero withdrawals started against
// them yet — untouched stock still fully sitting there. Doesn't
// account for partially-withdrawn deposits.
public record FreeZoneBreakdown(string FreeZoneName, int DepositCount, int WithdrawalCount, double? DaysOfInventory);

public record DepartmentPerformanceResponse(
    int OrderCount, decimal OrderValueUsd, decimal ExecutionPercent,
    int DraftCount, int InTransitCount, int UnderClearanceCount, int DeliveredCount,
    decimal DraftValueUsd, decimal InTransitValueUsd, decimal UnderClearanceValueUsd, decimal DeliveredValueUsd,
    int DepositCount, int WithdrawalCount, List<FreeZoneBreakdown> FreeZoneBreakdowns,
    int ShipmentsHitCount, decimal TotalDemurrageStorageUsd, decimal TotalShipmentValueUsd, decimal OverallMagnitudePercent,
    List<DemurrageHitDetail> HitDetails);

// Period filtering anchors on each shipment's ETA — "the actual
// shipments in the pipeline during that window" — not order creation
// date or completion date, which would each tell a different story.
[ApiController]
[Route("api/dashboards/department-performance")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.IpSupervisor)]
public class DepartmentPerformanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly Dictionary<int, decimal> _fxCache = new();
    public DepartmentPerformanceController(ShippingPortalDbContext db) => _db = db;

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        if (_fxCache.TryGetValue(currencyId.Value, out var cached)) return cached;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var value = rate?.RateToUsd ?? 1m;
        _fxCache[currencyId.Value] = value;
        return value;
    }

    [HttpGet]
    public async Task<ActionResult<DepartmentPerformanceResponse>> Get(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess,
        [FromQuery] DateOnly? etaFrom, [FromQuery] DateOnly? etaTo,
        [FromQuery] int? businessUnitId, [FromQuery] int? consigneeId, [FromQuery] int? shippingLineId)
    {
        var query = _db.Shipments
            .Where(s => s.Status != ShipmentStatus.Cancelled)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem)
            .AsQueryable();

        if (etaFrom.HasValue) query = query.Where(s => s.Eta >= etaFrom);
        if (etaTo.HasValue) query = query.Where(s => s.Eta <= etaTo);
        if (businessUnitId.HasValue) query = query.Where(s => s.PurchaseOrder!.BusinessUnitId == businessUnitId);
        if (consigneeId.HasValue) query = query.Where(s => s.PurchaseOrder!.ConsigneeId == consigneeId);
        if (shippingLineId.HasValue) query = query.Where(s => s.ShippingLineId == shippingLineId);

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        // A shipment can combine line items from more than one PO — count
        // every PO it actually touches (via the join table), not just its
        // single "primary" PurchaseOrderId, so a combined shipment's
        // secondary PO(s) aren't invisible to the Orders count/value here.
        var poIds = await _db.ShipmentPurchaseOrders
            .Where(spo => shipmentIds.Contains(spo.ShipmentId))
            .Select(spo => spo.PurchaseOrderId)
            .Distinct()
            .ToListAsync();

        // --- Orders: distinct POs behind this shipment set, execution % ---
        var pos = await _db.PurchaseOrders.Where(p => poIds.Contains(p.Id)).Include(p => p.LineItems).ToListAsync();
        var orderCount = pos.Count;
        var orderValueUsd = pos.SelectMany(p => p.LineItems).Sum(li => li.TotalUsd);
        var totalOrderedQty = pos.SelectMany(p => p.LineItems).Sum(li => li.Qty);
        var totalDispatchedQty = shipments.SelectMany(s => s.LineItems).Sum(li => li.QtyInBl);
        var executionPercent = totalOrderedQty == 0 ? 0 : Math.Min(100m, (totalDispatchedQty / totalOrderedQty) * 100m);

        // --- Shipments: status buckets (same derivation as Shipment Dashboard) ---
        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var deliveryOrders = await _db.ClearanceDeliveryOrders.Where(d => clearanceIds.Contains(d.ClearanceId)).ToDictionaryAsync(d => d.ClearanceId);
        var route1Completions = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route2Completions = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route3Completions = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);

        int draftCount = 0, inTransitCount = 0, underClearanceCount = 0, deliveredCount = 0;
        decimal draftValueUsd = 0, inTransitValueUsd = 0, underClearanceValueUsd = 0, deliveredValueUsd = 0;

        foreach (var s in shipments)
        {
            var itemsValueUsd = 0m;
            foreach (var li in s.LineItems)
            {
                var rate = await GetFxRateAsync(li.PurchaseOrderLineItem?.CurrencyId);
                itemsValueUsd += rate == 0 ? li.ItemSubtotal : li.ItemSubtotal / rate;
            }

            clearances.TryGetValue(s.Id, out var clearance);
            var deliveryOrder = clearance is not null ? deliveryOrders.GetValueOrDefault(clearance.Id) : null;
            var routeCompletion = clearance is null ? null : clearance.Route switch
            {
                ClearanceRouteType.Route1ClearAtPort => route1Completions.GetValueOrDefault(clearance.Id),
                ClearanceRouteType.Route2FzDeposit => route2Completions.GetValueOrDefault(clearance.Id),
                ClearanceRouteType.Route3ClearFromFz => route3Completions.GetValueOrDefault(clearance.Id),
                _ => null
            };

            if (s.Status == ShipmentStatus.Draft) { draftCount++; draftValueUsd += itemsValueUsd; }
            else if (routeCompletion.HasValue) { deliveredCount++; deliveredValueUsd += itemsValueUsd; }
            else if (deliveryOrder?.ActualArrivalDate.HasValue == true) { underClearanceCount++; underClearanceValueUsd += itemsValueUsd; }
            else { inTransitCount++; inTransitValueUsd += itemsValueUsd; }
        }

        // --- FZ activity: deposits (Route 2) and withdrawals against this shipment set, broken down by FZ name ---
        var depositCount = clearances.Values.Count(c => c.Route == ClearanceRouteType.Route2FzDeposit);
        var withdrawalCount = await _db.Withdrawals.Where(w => shipmentIds.Contains(w.DepositShipmentId)).CountAsync();

        var depositClearanceIds = clearances.Values.Where(c => c.Route == ClearanceRouteType.Route2FzDeposit).Select(c => c.Id).ToList();
        var route2DetailsForFz = await _db.ClearanceRoute2Details
            .Where(r => depositClearanceIds.Contains(r.ClearanceId))
            .Include(r => r.Destination)
            .ToListAsync();

        // Batched once upfront, not per-FZ, to avoid N+1 queries.
        var allWithdrawnDepositShipmentIds = (await _db.Withdrawals.Select(w => w.DepositShipmentId).Distinct().ToListAsync()).ToHashSet();
        var clearanceIdToShipmentId = clearances.Values.ToDictionary(c => c.Id, c => c.ShipmentId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Every withdrawal against any shipment deposited into this FZ,
        // counted per FZ using the same allWithdrawnDepositShipmentIds
        // source rather than a fresh query per group.
        var withdrawalsByDepositShipment = await _db.Withdrawals
            .GroupBy(w => w.DepositShipmentId)
            .Select(g => new { ShipmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Count);

        var fzBreakdowns = route2DetailsForFz
            .Where(r => r.Destination is not null)
            .GroupBy(r => r.Destination!.Name)
            .Select(g =>
            {
                var fzShipmentIds = g
                    .Select(r => clearanceIdToShipmentId.GetValueOrDefault(r.ClearanceId))
                    .Where(id => id != 0)
                    .ToList();

                var fzWithdrawalCount = fzShipmentIds.Sum(id => withdrawalsByDepositShipment.GetValueOrDefault(id, 0));

                var untouchedAges = g
                    .Where(r => r.ContainersReceivedAtFzDate.HasValue)
                    .Where(r => !allWithdrawnDepositShipmentIds.Contains(clearanceIdToShipmentId.GetValueOrDefault(r.ClearanceId)))
                    .Select(r => (double)(today.DayNumber - r.ContainersReceivedAtFzDate!.Value.DayNumber))
                    .ToList();

                return new FreeZoneBreakdown(
                    g.Key, g.Count(), fzWithdrawalCount,
                    untouchedAges.Count > 0 ? untouchedAges.Average() : null);
            })
            .OrderByDescending(f => f.DepositCount)
            .ToList();

        // --- Demurrage & Storage impact ---
        var actualCharges = await _db.ClearanceActualCharges.Where(c => clearanceIds.Contains(c.ClearanceId)).ToListAsync();
        var invoiceSummaries = await BuildInvoiceSummariesAsync(shipmentIds);

        var hitDetails = new List<DemurrageHitDetail>();
        decimal totalDemurrageStorageUsd = 0;
        foreach (var charges in actualCharges)
        {
            var hitSdg = (charges.ActualDemurragePaidSdg ?? 0) + (charges.ActualStoragePaidSdg ?? 0);
            if (hitSdg <= 0) continue;

            var clearanceEntry = clearances.Values.FirstOrDefault(c => c.Id == charges.ClearanceId);
            if (clearanceEntry is null) continue;
            var shipment = shipments.FirstOrDefault(s => s.Id == clearanceEntry.ShipmentId);
            if (shipment is null) continue;

            var sdgRate = await GetFxRateAsync((await _db.Currencies.FirstOrDefaultAsync(c => c.Code == "SDG"))?.Id);
            var hitUsd = sdgRate == 0 ? hitSdg : hitSdg / sdgRate;
            var shipmentValueUsd = invoiceSummaries.GetValueOrDefault(shipment.Id, 0m);
            var magnitudePercent = shipmentValueUsd == 0 ? 0 : (hitUsd / shipmentValueUsd) * 100m;

            totalDemurrageStorageUsd += hitUsd;
            hitDetails.Add(new DemurrageHitDetail(
                shipment.BlAwbNo, shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", hitUsd, shipmentValueUsd, magnitudePercent));
        }

        var totalShipmentValueUsd = draftValueUsd + inTransitValueUsd + underClearanceValueUsd + deliveredValueUsd;
        var overallMagnitudePercent = totalShipmentValueUsd == 0 ? 0 : (totalDemurrageStorageUsd / totalShipmentValueUsd) * 100m;

        return Ok(new DepartmentPerformanceResponse(
            orderCount, orderValueUsd, executionPercent,
            draftCount, inTransitCount, underClearanceCount, deliveredCount,
            draftValueUsd, inTransitValueUsd, underClearanceValueUsd, deliveredValueUsd,
            depositCount, withdrawalCount, fzBreakdowns,
            hitDetails.Count, totalDemurrageStorageUsd, totalShipmentValueUsd, overallMagnitudePercent,
            hitDetails.OrderByDescending(h => h.MagnitudePercent).ToList()));
    }

    // Mirrors GetSupplierInvoiceSummary's own simplification: one
    // currency per shipment, taken from its first line item.
    private async Task<Dictionary<int, decimal>> BuildInvoiceSummariesAsync(List<int> shipmentIds)
    {
        var lineItems = await _db.ShipmentLineItems
            .Where(li => shipmentIds.Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem)
            .ToListAsync();
        var byShipment = lineItems.GroupBy(li => li.ShipmentId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, decimal>();
        foreach (var (shipmentId, items) in byShipment)
        {
            var invoiceValue = items.Sum(li => li.ItemSubtotal);
            var firstCurrencyId = items.FirstOrDefault()?.PurchaseOrderLineItem?.CurrencyId;
            var rate = await GetFxRateAsync(firstCurrencyId);
            result[shipmentId] = rate == 0 ? invoiceValue : invoiceValue / rate;
        }
        return result;
    }
}
