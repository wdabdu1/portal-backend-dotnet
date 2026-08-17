using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.SupplierDues;

public record SupplierDueRow(
    int ShipmentId, string BusinessUnit, string SupplierName, string PoNumber, string? SupplierInvoiceNo,
    string BlAwbNo, DateOnly? Sob, decimal InvoiceValue, string InvoiceCurrency,
    decimal TotalValueUsd, decimal TotalUnpaidUsd,
    // Earliest due date still outstanding (paid < owed on that specific
    // due), same per-due tracking logic as the detail panel's own
    // Payment Due Schedule — not just "any money still owed overall".
    DateOnly? NextPaymentDate, decimal? NextPaymentValueUsd);

[ApiController]
[Authorize(Roles = AppRoles.SupplierDuesViewers)]
[Route("api/supplier-dues")]
public class SupplierDuesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SupplierDuesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDueRow>>> GetOpen([FromServices] BuAccessService buAccess, [FromQuery] string status = "Pending")
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

        var shipmentIdsForDues = shipments.Select(s => s.Id).ToList();
        var duesByShipment = await _db.ShipmentPaymentDues
            .Where(d => shipmentIdsForDues.Contains(d.ShipmentId))
            .Include(d => d.Currency)
            .OrderBy(d => d.DueDate)
            .GroupBy(d => d.ShipmentId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        var paidByDue = await _db.ShipmentSupplierPaymentRecords
            .Where(r => shipmentIdsForDues.Contains(r.ShipmentId) && r.PaymentDueId != null)
            .GroupBy(r => r.PaymentDueId!.Value)
            .Select(g => new { DueId = g.Key, PaidUsd = g.Sum(r => r.ValueUsd) })
            .ToDictionaryAsync(x => x.DueId, x => x.PaidUsd);

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

            // "Pending" (default) = anything NOT exactly settled — owed
            // (positive) OR overpaid (negative) — so an overpayment
            // stays visible as something needing action, not silently
            // grouped in with genuinely-closed shipments. "Closed" =
            // fully settled (balance is zero, within rounding). "All" =
            // both.
            var isClosed = Math.Abs(totalUnpaidUsd) < 0.01m;
            if (status == "Pending" && isClosed) continue;
            if (status == "Closed" && !isClosed) continue;

            fullSets.TryGetValue(shipment.Id, out var fullSet);
            var po = shipment.PurchaseOrder!;

            DateOnly? nextPaymentDate = null;
            decimal? nextPaymentValueUsd = null;
            if (duesByShipment.TryGetValue(shipment.Id, out var dues))
            {
                foreach (var due in dues)
                {
                    var dueRate = due.CurrencyId > 0 ? await RateFor(due.CurrencyId) : 1m;
                    var dueAmountUsd = due.Amount / dueRate;
                    var duePaidUsd = paidByDue.GetValueOrDefault(due.Id, 0m);
                    if (duePaidUsd < dueAmountUsd - 0.01m)
                    {
                        nextPaymentDate = due.DueDate;
                        nextPaymentValueUsd = dueAmountUsd - duePaidUsd;
                        break; // dues are pre-sorted by date, so the first outstanding one is the next one
                    }
                }
            }

            rows.Add(new SupplierDueRow(
                shipment.Id, po.BusinessUnit!.Name, po.Supplier!.Name, po.PoNumber, fullSet?.SupplierInvoiceNo,
                shipment.BlAwbNo, shipment.SobActualDate,
                invoiceValue, currencyCode, invoiceValueUsd, totalUnpaidUsd,
                nextPaymentDate, nextPaymentValueUsd));
        }

        return Ok(rows.OrderBy(r => r.NextPaymentDate ?? DateOnly.MaxValue).ToList());
    }
}
