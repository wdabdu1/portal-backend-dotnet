using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record CustomsClearancePaymentRow(string BusinessUnit, string ChargeType, decimal ValueSdg, DateOnly? DueDate, string BlAwbNo);

public record PoDashboardShipmentRow(
    string BlAwbNo, string Category, string ModelProduct, decimal Qty, decimal UnitPrice, string Currency,
    decimal Total, DateOnly? Eta, DateOnly? Etd, DateOnly? ExpectedClearanceCompletion);

public record PoDashboardRow(
    int Id, string PoNumber, string BusinessUnit, string Supplier, string Consignee, string Status,
    DateTime CreatedAt, decimal OrderValueUsd, List<PoDashboardShipmentRow> Shipments);

public record ClearanceDashboardRow(
    string BlAwbNo, decimal ClearanceProgressPercent, string BusinessUnit, string Category, string ModelProduct,
    decimal Qty, int Fcl20Count, int Fcl40Count, DateOnly? Eta, DateOnly? ClearanceCompletionDate,
    int? DaysRemaining, string Route, string ClearanceFrom, string Status);

public record GoodsInTransitRow(
    string BusinessUnit, string Category, string ModelProduct, decimal Qty, string PickFrom,
    DateOnly PickupDate, string DropOffCity, string DropOffWarehouse, DateOnly? DropOffTargetDate,
    DateOnly? DropOffActualDate, string TruckNo, string DriverName, string Status);
    DateTime OrderCreationDate, string CurrentStatus, string BusinessUnit, string BlAwbNo, string PoNumber,
    string Category, string ModelProduct, decimal Qty, decimal UnitPrice, string Currency, decimal Total,
    decimal PaidUsd, decimal BalanceUnpaidUsd, DateOnly? Eta, DateOnly? Etd, DateOnly? ClearanceCompletionDate);

public record SupplierPaymentRow(string BusinessUnit, string SupplierName, string BlAwbNo, DateOnly DueDate, string Label, decimal AmountUsd);

[ApiController]
[Route("api/dashboards")]
[Authorize]
public class DashboardsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly Dictionary<int, decimal> _fxCache = new();

    public DashboardsController(ShippingPortalDbContext db) => _db = db;

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        if (_fxCache.TryGetValue(currencyId.Value, out var cached)) return cached;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var value = rate?.RateToUsd ?? 1m;
        _fxCache[currencyId.Value] = value;
        return value;
    }

[HttpGet("purchase-orders")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.Bu + "," + AppRoles.CorpFinance)]
    public async Task<ActionResult<IEnumerable<PoDashboardRow>>> GetPurchaseOrders(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess,
        [FromServices] ShippingPortal.Api.Services.ClearanceScheduleService scheduleService)
    {
        var query = _db.PurchaseOrders
            .Include(p => p.BusinessUnit)
            .Include(p => p.Supplier)
            .Include(p => p.Consignee)
            .Include(p => p.LineItems)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(p => allowedBus.Contains(p.BusinessUnitId));
        }

        var pos = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        var poIds = pos.Select(p => p.Id).ToList();

        var shipments = await _db.Shipments
            .Where(s => poIds.Contains(s.PurchaseOrderId))
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .ToListAsync();
        var shipmentsByPo = shipments.GroupBy(s => s.PurchaseOrderId).ToDictionary(g => g.Key, g => g.ToList());

        var estimatedCompletions = await scheduleService.GetEstimatedCompletionDatesAsync(shipments.Select(s => s.Id).ToList());

        var result = pos.Select(p => new PoDashboardRow(
            p.Id, p.PoNumber, p.BusinessUnit?.Name ?? "", p.Supplier?.Name ?? "", p.Consignee?.Name ?? "", p.Status.ToString(),
            p.CreatedAt, p.LineItems.Sum(li => li.TotalUsd),
            shipmentsByPo.GetValueOrDefault(p.Id, new List<ShippingPortal.Api.Models.Shipments.Shipment>())
                .SelectMany(s => s.LineItems.Select(li => new PoDashboardShipmentRow(
                    s.BlAwbNo, li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                    li.QtyInBl, li.PurchaseOrderLineItem?.UnitPrice ?? 0, li.PurchaseOrderLineItem?.Currency?.Code ?? "",
                    li.ItemSubtotal, s.Eta, s.Etd, estimatedCompletions.GetValueOrDefault(s.Id)
                ))).ToList()
        )).ToList();

        return Ok(result);
    }

    [HttpGet("shipments")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.Bu + "," + AppRoles.CorpFinance)]
    public async Task<ActionResult<IEnumerable<ShipmentDashboardRow>>> GetShipments(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess,
        [FromServices] ShippingPortal.Api.Services.ClearanceScheduleService scheduleService)
    {
        var query = _db.Shipments
            .Where(s => s.Status != ShippingPortal.Api.Models.Shipments.ShipmentStatus.Cancelled)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        // Status derivation inputs — arrival + route-specific completion.
        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var deliveryOrders = await _db.ClearanceDeliveryOrders.Where(d => clearanceIds.Contains(d.ClearanceId)).ToDictionaryAsync(d => d.ClearanceId);
        var route1Completions = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route2Completions = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route3Completions = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);

        var estimatedCompletions = await scheduleService.GetEstimatedCompletionDatesAsync(shipmentIds);

        // Paid/Balance — same simplification as the single-shipment
        // summary: one currency per shipment, taken from its first item.
        var lineItemsByShipment = shipments.ToDictionary(s => s.Id, s => s.LineItems.ToList());
        var paymentRecords = await _db.ShipmentSupplierPaymentRecords.Where(r => shipmentIds.Contains(r.ShipmentId)).ToListAsync();
        var paidByShipment = paymentRecords.GroupBy(r => r.ShipmentId).ToDictionary(g => g.Key, g => g.Sum(r => r.ValueUsd));

        var result = new List<ShipmentDashboardRow>();
        foreach (var s in shipments)
        {
            clearances.TryGetValue(s.Id, out var clearance);
            var deliveryOrder = clearance is not null ? deliveryOrders.GetValueOrDefault(clearance.Id) : null;

            DateOnly? routeCompletion = clearance is null ? null : clearance.Route switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => route1Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => route2Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz => route3Completions.GetValueOrDefault(clearance.Id),
                _ => null
            };

            string status;
            if (s.Status == ShippingPortal.Api.Models.Shipments.ShipmentStatus.Draft) status = "Draft";
            else if (routeCompletion.HasValue) status = "Delivered";
            else if (deliveryOrder?.ActualArrivalDate.HasValue == true) status = "Under Clearance";
            else status = "In Transit";

            var itemsForShipment = lineItemsByShipment.GetValueOrDefault(s.Id, new List<ShippingPortal.Api.Models.Shipments.ShipmentLineItem>());
            var invoiceValueUsd = itemsForShipment.Sum(li => li.ItemSubtotal);
            var firstCurrencyId = itemsForShipment.FirstOrDefault()?.PurchaseOrderLineItem?.CurrencyId;
            var rate = await GetFxRateAsync(firstCurrencyId);
            invoiceValueUsd = rate == 0 ? invoiceValueUsd : invoiceValueUsd / rate;

            var paidUsd = paidByShipment.GetValueOrDefault(s.Id);
            var balanceUsd = invoiceValueUsd - paidUsd;
            var completion = routeCompletion ?? estimatedCompletions.GetValueOrDefault(s.Id);

            foreach (var li in itemsForShipment)
            {
                result.Add(new ShipmentDashboardRow(
                    s.CreatedAt, status, s.PurchaseOrder?.BusinessUnit?.Name ?? "", s.BlAwbNo, s.PurchaseOrder?.PoNumber ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                    li.QtyInBl, li.PurchaseOrderLineItem?.UnitPrice ?? 0, li.PurchaseOrderLineItem?.Currency?.Code ?? "",
                    li.ItemSubtotal, paidUsd, balanceUsd, s.Eta, s.Etd, completion));
            }
        }

        return Ok(result.OrderByDescending(r => r.OrderCreationDate).ToList());
    }

    [HttpGet("under-clearance")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<ClearanceDashboardRow>>> GetUnderClearance(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess,
        [FromServices] ShippingPortal.Api.Services.ClearanceScheduleService scheduleService)
    {
        var query = _db.Shipments
            .Where(s => s.Status == ShippingPortal.Api.Models.Shipments.ShipmentStatus.Confirmed)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();

        var route1Completions = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route2Completions = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route3Completions = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);

        // Route 2's own deposit location, plus Route 3's own withdrawal
        // traces back to whichever Route 2 shipment it drew from — both
        // resolve to the same underlying FZ location name.
        var route2Details = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).Include(r => r.Destination).ToDictionaryAsync(r => r.ClearanceId);
        var route3Details = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId);
        var depositShipmentIds = route3Details.Values.Where(r => r.DepositShipmentId.HasValue).Select(r => r.DepositShipmentId!.Value).ToList();
        var depositClearances = await _db.Clearances.Where(c => depositShipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var depositClearanceIds = depositClearances.Values.Select(c => c.Id).ToList();
        var depositRoute2Details = await _db.ClearanceRoute2Details.Where(r => depositClearanceIds.Contains(r.ClearanceId)).Include(r => r.Destination).ToDictionaryAsync(r => r.ClearanceId);

        // Route 1 defaults to "Port" — there's only one — but once
        // Logistics assigns a destination warehouse, that becomes the
        // more meaningful "from" location, and a still-missing one is
        // itself a useful early signal that trucking isn't planned yet.
        var warehouseAllocations = await _db.WarehouseAllocations
            .Where(w => w.ShipmentLineItem != null && shipmentIds.Contains(w.ShipmentLineItem.ShipmentId))
            .Include(w => w.Warehouse)
            .Include(w => w.ShipmentLineItem)
            .ToListAsync();
        var warehouseByShipment = warehouseAllocations
            .Where(w => w.ShipmentLineItem != null)
            .GroupBy(w => w.ShipmentLineItem!.ShipmentId)
            .ToDictionary(g => g.Key, g => g.First().Warehouse?.Name);

        var slaByDivision = await _db.ClearanceSlaSettings
            .Where(s => s.IsActive)
            .GroupBy(s => s.Division)
            .Select(g => new { Division = g.Key, Total = g.Sum(s => s.TargetDays) })
            .ToDictionaryAsync(x => x.Division, x => x.Total);
        var generalDays = slaByDivision.GetValueOrDefault(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, 0);

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var estimatedCompletions = await scheduleService.GetEstimatedCompletionDatesAsync(shipmentIds);

        var result = new List<ClearanceDashboardRow>();
        foreach (var s in shipments)
        {
            clearances.TryGetValue(s.Id, out var clearance);
            if (clearance is null || clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.NotSelected) continue;

            var routeDivision = clearance.Route switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1,
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2,
                _ => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3
            };
            var routeDays = slaByDivision.GetValueOrDefault(routeDivision, 0);
            var targetDays = routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3 ? routeDays : generalDays + routeDays;

            DateOnly? actualCompletedDate = routeDivision switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1 => route1Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2 => route2Completions.GetValueOrDefault(clearance.Id),
                _ => route3Completions.GetValueOrDefault(clearance.Id)
            };

            var slaPercent = ComputeSlaPercentLocal(s.Eta, actualCompletedDate, targetDays);
            var status = actualCompletedDate.HasValue ? "Completed" : "Under Process";

            string clearanceFrom;
            if (routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1)
            {
                clearanceFrom = warehouseByShipment.GetValueOrDefault(s.Id) ?? "Port";
            }
            else if (routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2)
            {
                clearanceFrom = route2Details.GetValueOrDefault(clearance.Id)?.Destination?.Name ?? "";
            }
            else
            {
                var depositShipmentId = route3Details.GetValueOrDefault(clearance.Id)?.DepositShipmentId;
                var depositClearance = depositShipmentId.HasValue ? depositClearances.GetValueOrDefault(depositShipmentId.Value) : null;
                clearanceFrom = depositClearance is not null ? (depositRoute2Details.GetValueOrDefault(depositClearance.Id)?.Destination?.Name ?? "") : "";
            }

            var completionDate = actualCompletedDate ?? estimatedCompletions.GetValueOrDefault(s.Id);
            int? daysRemaining = completionDate.HasValue ? BusinessDaysBetweenLocal(today, completionDate.Value, holidaySet) : null;

            var firstLine = s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem;
            var totalQty = s.LineItems.Sum(li => li.QtyInBl);

            result.Add(new ClearanceDashboardRow(
                s.BlAwbNo, slaPercent, s.PurchaseOrder?.BusinessUnit?.Name ?? "",
                firstLine?.ProductCategory?.Name ?? "", firstLine?.ModelProduct?.Name ?? "", totalQty,
                s.Fcl20Count, s.Fcl40Count, s.Eta, completionDate, daysRemaining,
                clearance.Route.ToString(), clearanceFrom, status));
        }

        return Ok(result.OrderBy(r => r.DaysRemaining ?? int.MaxValue).ToList());
    }

    private static decimal ComputeSlaPercentLocal(DateOnly? eta, DateOnly? clearanceCompleteDate, decimal targetDays)
    {
        if (clearanceCompleteDate.HasValue) return 100m;
        if (!eta.HasValue || targetDays <= 0) return 0m;
        var daysSinceEta = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - eta.Value.DayNumber;
        return Math.Max(0m, (daysSinceEta / targetDays) * 100m);
    }

    private static int BusinessDaysBetweenLocal(DateOnly from, DateOnly to, HashSet<DateOnly> holidays)
    {
        if (from == to) return 0;
        var forward = to > from;
        var start = forward ? from : to;
        var end = forward ? to : from;
        var count = 0;
        var date = start;
        while (date < end)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday) continue;
            if (holidays.Contains(date)) continue;
            count++;
        }
        return forward ? count : -count;
    }

    // Pulled from Cost Estimates only, no settlement filtering — this is
    // a budgeting view, not a payment-tracking one.
    [HttpGet("customs-clearance-payments")]

    // Pulled from Cost Estimates only, no settlement filtering — this is
    // a budgeting view, not a payment-tracking one.
    [HttpGet("customs-clearance-payments")]

    // Pulled from Cost Estimates only, no settlement filtering — this is
    // a budgeting view, not a payment-tracking one.
    [HttpGet("customs-clearance-payments")]
    [Authorize(Roles = AppRoles.CorpFinance + "," + AppRoles.Treasury + "," + AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<IEnumerable<CustomsClearancePaymentRow>>> GetCustomsClearancePayments(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        var query = _db.ClearanceEstimateLineItems
            .Include(li => li.ChargeType)
            .Include(li => li.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(li => allowedBus.Contains(li.Clearance!.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var rows = await query.ToListAsync();

        return Ok(rows.Select(li => new CustomsClearancePaymentRow(
            li.Clearance?.Shipment?.PurchaseOrder?.BusinessUnit?.Name ?? "",
            li.ChargeType?.Name ?? "",
            li.ValueSdg,
            li.DueDate,
            li.Clearance?.Shipment?.BlAwbNo ?? ""
        )).ToList());
    }

    // Every open shipment's payment due schedule, rolled up across the
    // whole portfolio — converted to USD so mixed-currency rows can be
    // accumulated together. View grouping (monthly / next 8 weeks / all)
    // happens on the frontend from this same flat list.
    [HttpGet("supplier-payments")]
    [Authorize(Roles = AppRoles.CorpFinance + "," + AppRoles.Treasury + "," + AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<IEnumerable<SupplierPaymentRow>>> GetSupplierPayments(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        var query = _db.ShipmentPaymentDues
            .Include(d => d.Currency)
            .Include(d => d.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(d => d.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(d => allowedBus.Contains(d.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var dues = await query.ToListAsync();

        var result = new List<SupplierPaymentRow>();
        foreach (var d in dues)
        {
            var rate = await GetFxRateAsync(d.CurrencyId);
            var amountUsd = rate == 0 ? d.Amount : d.Amount / rate;
            result.Add(new SupplierPaymentRow(
                d.Shipment?.PurchaseOrder?.BusinessUnit?.Name ?? "",
                d.Shipment?.PurchaseOrder?.Supplier?.Name ?? "",
                d.Shipment?.BlAwbNo ?? "",
                d.DueDate,
                d.Label ?? "",
                amountUsd
            ));
        }
        return Ok(result.OrderBy(r => r.DueDate).ToList());
    }
}
