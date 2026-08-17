using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Services;

// Keeps each shipment's own auto-inserted Advance line in sync with its
// PO's Advance Payment configuration. Two entry points: SyncOneAsync is
// called whenever a single shipment's Payment Due Schedule is opened
// (inserts the line silently if missing); SyncAllForPoAsync is called
// whenever the PO's own advance is edited (propagates the real
// execution date/amount out to every shipment that already has one).
public class PoAdvancePaymentService
{
    private readonly ShippingPortalDbContext _db;
    public PoAdvancePaymentService(ShippingPortalDbContext db) => _db = db;

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        return rate?.RateToUsd ?? 1m;
    }

    private async Task<int> GetUsdCurrencyIdAsync() =>
        await _db.Currencies.Where(c => c.Code == "USD").Select(c => c.Id).FirstOrDefaultAsync();

    // Same computation as GetSupplierInvoiceSummary — sum of the
    // shipment's own line items, converted to USD.
    private async Task<decimal> GetShipmentInvoiceValueUsdAsync(int shipmentId)
    {
        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem)
            .ToListAsync();

        var invoiceValue = lineItems.Sum(li => li.ItemSubtotal);
        var currencyId = lineItems.FirstOrDefault()?.PurchaseOrderLineItem?.CurrencyId;
        var rate = await GetFxRateAsync(currencyId);
        return invoiceValue / rate;
    }

    public async Task SyncOneAsync(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return;

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == shipment.PurchaseOrderId);
        if (po is null || !po.AdvancePaymentPercent.HasValue || po.AdvancePaymentPercent.Value <= 0) return;

        var existing = await _db.ShipmentPaymentDues.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId && d.IsFromPoAdvance);
        var dueDate = po.AdvancePaymentExecutedDate ?? po.AdvancePaymentPlannedDate;
        if (!dueDate.HasValue) return; // nothing to anchor the line to yet

        var invoiceValueUsd = await GetShipmentInvoiceValueUsdAsync(shipmentId);
        var amountUsd = invoiceValueUsd * (po.AdvancePaymentPercent.Value / 100m);
        var usdId = await GetUsdCurrencyIdAsync();
        var label = po.AdvancePaymentExecutedDate.HasValue
            ? $"Advance ({po.AdvancePaymentPercent.Value:0.##}% — from PO, settled)"
            : $"Advance ({po.AdvancePaymentPercent.Value:0.##}% — from PO, planned)";

        if (existing is null)
        {
            _db.ShipmentPaymentDues.Add(new ShipmentPaymentDue
            {
                ShipmentId = shipmentId,
                DueDate = dueDate.Value,
                Amount = amountUsd,
                CurrencyId = usdId,
                Label = label,
                IsFromPoAdvance = true
            });
        }
        else
        {
            existing.DueDate = dueDate.Value;
            existing.Amount = amountUsd;
            existing.CurrencyId = usdId;
            existing.Label = label;
        }

        await _db.SaveChangesAsync();
    }

    public async Task SyncAllForPoAsync(int purchaseOrderId)
    {
        var shipmentIds = await _db.Shipments
            .Where(s => s.PurchaseOrderId == purchaseOrderId)
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var id in shipmentIds)
            await SyncOneAsync(id);
    }
}
