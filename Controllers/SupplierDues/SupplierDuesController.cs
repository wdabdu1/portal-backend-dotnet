using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.SupplierDues;

public record SupplierDueRow(
    int ShipmentId, string BusinessUnit, string SupplierName, string PoNumber, string? SupplierInvoiceNo,
    string BlAwbNo, DateOnly? Sob, string? PaymentTerm, decimal InvoiceValue, string InvoiceCurrency,
    decimal TotalValueUsd, decimal TotalUnpaidUsd);

[ApiController]
[Authorize(Roles = AppRoles.SupplierDuesViewers)]
[Route("api/supplier-dues")]
public class SupplierDuesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SupplierDuesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDueRow>>> GetOpen([FromServices] BuAccessService buAccess)
    {
        var query = _db.Shipments
            .Where(s => s.Status != ShipmentStatus.Cancelled)
            .Include(s => s.PurchaseOrder).ThenInclude(po => po!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(po => po!.Supplier)
            .Include(s => s.PurchaseOrder).ThenInclude(po => po!.SupplierPaymentTerm)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipments = await query.ToListAsync();

        var fullSets = await _db.ShipmentSupplierFullSets.ToDictionaryAsync(f => f.ShipmentId);
        var paymentsByShipment = await _db.ShipmentSupplierPaymentRecords
            .GroupBy(r => r.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, TotalPaidUsd = g.Sum(r => r.ValueUsd) })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.TotalPaidUsd);

        var fxCache = new Dictionary<int, decimal>();
        async Task<decimal> RateFor(int currencyId)
        {
            if (fxCache.TryGetValue(currencyId, out var cached)) return cached;
            var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
            var value = rate?.RateToUsd ?? 1m;
            fxCache[currencyId] = value;
            return value;
        }

        var rows = new List<SupplierDueRow>();
        foreach (var shipment in shipments)
        {
            if (shipment.LineItems.Count == 0) continue;

            var invoiceValue = shipment.LineItems.Sum(li => li.ItemSubtotal);
            var firstLine = shipment.LineItems.First().PurchaseOrderLineItem;
            var currencyId = firstLine?.CurrencyId ?? 0;
            var currencyCode = firstLine?.Currency?.Code ?? "";
            var rate = currencyId > 0 ? await RateFor(currencyId) : 1m;
            var invoiceValueUsd = invoiceValue / rate;

            var totalPaidUsd = paymentsByShipment.GetValueOrDefault(shipment.Id, 0m);
            var totalUnpaidUsd = invoiceValueUsd - totalPaidUsd;

            if (totalUnpaidUsd <= 0) continue;

            fullSets.TryGetValue(shipment.Id, out var fullSet);
            var po = shipment.PurchaseOrder!;

            rows.Add(new SupplierDueRow(
                shipment.Id, po.BusinessUnit!.Name, po.Supplier!.Name, po.PoNumber, fullSet?.SupplierInvoiceNo,
                shipment.BlAwbNo, shipment.SobActualDate, po.SupplierPaymentTerm?.Name,
                invoiceValue, currencyCode, invoiceValueUsd, totalUnpaidUsd));
        }

        return Ok(rows.OrderBy(r => r.SupplierName).ToList());
    }
}
