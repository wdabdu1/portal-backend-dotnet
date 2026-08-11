using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

ppublic record CustomsClearancePaymentRow(string BusinessUnit, string ChargeType, decimal ValueSdg, DateOnly? DueDate, string BlAwbNo);

public record PoDashboardShipmentRow(
    string BlAwbNo, string Category, string ModelProduct, decimal Qty, decimal UnitPrice, string Currency,
    decimal Total, DateOnly? Eta, DateOnly? Etd, DateOnly? ExpectedClearanceCompletion);

public record PoDashboardRow(
    int Id, string PoNumber, string BusinessUnit, string Supplier, string Consignee, string Status,
    DateTime CreatedAt, decimal OrderValueUsd, List<PoDashboardShipmentRow> Shipments);

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
            .Include(p => p.Shipments).ThenInclude(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(p => p.Shipments).ThenInclude(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(p => p.Shipments).ThenInclude(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(p => allowedBus.Contains(p.BusinessUnitId));
        }

        var pos = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        var allShipmentIds = pos.SelectMany(p => p.Shipments).Select(s => s.Id).ToList();
        var estimatedCompletions = await scheduleService.GetEstimatedCompletionDatesAsync(allShipmentIds);

        var result = pos.Select(p => new PoDashboardRow(
            p.Id, p.PoNumber, p.BusinessUnit?.Name ?? "", p.Supplier?.Name ?? "", p.Consignee?.Name ?? "", p.Status.ToString(),
            p.CreatedAt, p.LineItems.Sum(li => li.TotalUsd),
            p.Shipments.SelectMany(s => s.LineItems.Select(li => new PoDashboardShipmentRow(
                s.BlAwbNo, li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                li.QtyInBl, li.PurchaseOrderLineItem?.UnitPrice ?? 0, li.PurchaseOrderLineItem?.Currency?.Code ?? "",
                li.ItemSubtotal, s.Eta, s.Etd, estimatedCompletions.GetValueOrDefault(s.Id)
            ))).ToList()
        )).ToList();

        return Ok(result);
    }

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
