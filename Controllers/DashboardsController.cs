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
