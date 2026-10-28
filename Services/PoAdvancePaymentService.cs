using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Services;

// Keeps every shipment's "from PO" payment lines in sync with its PO's
// payments (PurchaseOrderPayments). A PO can now carry several payments;
// each one produces ONE line in each linked shipment's Payment Due
// Schedule, sized pro rata to that shipment's share of the PO:
//
//     due (USD) = payment.AmountUsd × (shipment's lines of this PO ÷ whole PO)
//
// A line is tied back to its payment through the ShipmentPaymentDues
// column PurchaseOrderPaymentId (a shadow property — see DbContext).
//
// Entry points: SyncOneAsync (a shipment's Payment Due Schedule is
// opened) and SyncAllForPoAsync (a PO payment was added/edited).
public class PoAdvancePaymentService
{
    private const string LinkProp = "PurchaseOrderPaymentId";
    private readonly ShippingPortalDbContext _db;
    public PoAdvancePaymentService(ShippingPortalDbContext db) => _db = db;

    // ---- FX / PO value basis ------------------------------------------

    public async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var v = rate?.RateToUsd ?? 1m;
        return v == 0 ? 1m : v;
    }

    private async Task<int> GetUsdCurrencyIdAsync() =>
        await _db.Currencies.Where(c => c.Code == "USD").Select(c => c.Id).FirstOrDefaultAsync();

    // How big the PO is. When every line is in ONE currency the PO is
    // measured natively in that currency (so % = value ÷ value exactly,
    // no FX noise). A mixed-currency PO falls back to USD at current rates.
    public class PoBasis
    {
        public int PurchaseOrderId { get; set; }
        public int? SingleCurrencyId { get; set; }   // set when all lines share one currency
        public int DefaultCurrencyId { get; set; }   // single currency, or the first line's
        public decimal NativeTotal { get; set; }     // only meaningful when SingleCurrencyId is set
        public decimal UsdTotal { get; set; }        // at current rates
        public Dictionary<int, decimal> Rates { get; set; } = new();
    }

    public async Task<PoBasis> GetPoBasisAsync(int purchaseOrderId)
    {
        var lines = await _db.PurchaseOrderLineItems.Where(li => li.PurchaseOrderId == purchaseOrderId).OrderBy(li => li.Id).ToListAsync();
        var basis = new PoBasis { PurchaseOrderId = purchaseOrderId };
        foreach (var cid in lines.Select(l => l.CurrencyId).Distinct())
            basis.Rates[cid] = await GetFxRateAsync(cid);

        var currencies = lines.Select(l => l.CurrencyId).Distinct().ToList();
        basis.DefaultCurrencyId = lines.FirstOrDefault()?.CurrencyId ?? 0;
        if (currencies.Count == 1) basis.SingleCurrencyId = currencies[0];
        basis.NativeTotal = lines.Sum(l => l.Qty * l.UnitPrice);
        basis.UsdTotal = lines.Sum(l => l.Qty * l.UnitPrice / basis.Rates[l.CurrencyId]);
        return basis;
    }

    // % of the PO a payment represents. Exact (native) when the payment is
    // in the PO's single currency, otherwise via USD.
    public static decimal PercentOf(PurchaseOrderPayment p, PoBasis b)
    {
        if (b.SingleCurrencyId.HasValue && b.SingleCurrencyId.Value == p.CurrencyId)
            return b.NativeTotal > 0 ? p.Amount / b.NativeTotal * 100m : 0m;
        return b.UsdTotal > 0 ? p.AmountUsd / b.UsdTotal * 100m : 0m;
    }

    // ---- Shipment cascade ---------------------------------------------

    private static string BuildLabel(PurchaseOrderPayment p, decimal pct)
    {
        var state = p.ExecutedDate.HasValue ? "settled" : "planned";
        var label = $"{p.Description} ({pct:0.####}% — from PO, {state})";
        return label.Length > 100 ? label.Substring(0, 100) : label;
    }

    // The shipment's share of this PO (0..1). Only lines that belong to
    // this PO count, so a multi-PO shipment is split correctly.
    private async Task<decimal> GetShipmentShareAsync(int shipmentId, PoBasis basis)
    {
        var lines = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem)
            .ToListAsync();
        var mine = lines.Where(li => li.PurchaseOrderLineItem != null && li.PurchaseOrderLineItem.PurchaseOrderId == basis.PurchaseOrderId).ToList();
        if (mine.Count == 0) return 0m;

        decimal share;
        if (basis.SingleCurrencyId.HasValue)
            share = basis.NativeTotal > 0 ? mine.Sum(li => li.ItemSubtotal) / basis.NativeTotal : 0m;
        else
        {
            var usd = mine.Sum(li => li.ItemSubtotal / (basis.Rates.TryGetValue(li.PurchaseOrderLineItem!.CurrencyId, out var r) ? r : 1m));
            share = basis.UsdTotal > 0 ? usd / basis.UsdTotal : 0m;
        }
        return share > 1m ? 1m : (share < 0m ? 0m : share);
    }

    private int? LinkOf(ShipmentPaymentDue d) => _db.Entry(d).Property<int?>(LinkProp).CurrentValue;

    private async Task SyncShipmentForPoAsync(int shipmentId, int purchaseOrderId, bool isPrimaryPo, int usdId)
    {
        var payments = await _db.PurchaseOrderPayments.Where(p => p.PurchaseOrderId == purchaseOrderId).OrderBy(p => p.Id).ToListAsync();
        if (payments.Count == 0) return;

        var basis = await GetPoBasisAsync(purchaseOrderId);
        var share = await GetShipmentShareAsync(shipmentId, basis);

        var advanceDues = await _db.ShipmentPaymentDues.Where(d => d.ShipmentId == shipmentId && d.IsFromPoAdvance).ToListAsync();
        var paymentIds = payments.Select(p => p.Id).ToHashSet();
        // Unlinked lines are legacy ones (pre-multi-payment); only the shipment's
        // primary PO may adopt them, mirroring the old one-advance-per-PO behaviour.
        var orphans = isPrimaryPo ? advanceDues.Where(d => LinkOf(d) is null).ToList() : new List<ShipmentPaymentDue>();
        var first = true;

        foreach (var p in payments)
        {
            var dueDate = p.ExecutedDate ?? p.PlannedDate;
            var pct = PercentOf(p, basis);
            var label = BuildLabel(p, pct);

            var existing = advanceDues.FirstOrDefault(d => LinkOf(d) == p.Id);
            if (existing is null)
            {
                existing = orphans.FirstOrDefault(d => d.Label == label) ?? (first ? orphans.FirstOrDefault() : null);
                if (existing is not null) orphans.Remove(existing);
            }
            first = false;

            if (!dueDate.HasValue) continue; // nothing to anchor the line to yet

            var amountUsd = Math.Round(p.AmountUsd * share, 4);
            if (existing is null)
            {
                if (amountUsd <= 0) continue;
                var due = new ShipmentPaymentDue
                {
                    ShipmentId = shipmentId,
                    DueDate = dueDate.Value,
                    Amount = amountUsd,
                    CurrencyId = usdId,
                    Label = label,
                    IsFromPoAdvance = true
                };
                _db.ShipmentPaymentDues.Add(due);
                _db.Entry(due).Property<int?>(LinkProp).CurrentValue = p.Id;
            }
            else
            {
                existing.DueDate = dueDate.Value;
                existing.Amount = amountUsd;
                existing.CurrencyId = usdId;
                existing.Label = label;
                existing.IsFromPoAdvance = true;
                _db.Entry(existing).Property<int?>(LinkProp).CurrentValue = p.Id;
            }
        }

        await _db.SaveChangesAsync();
        await SyncPaymentRecordsAsync(shipmentId, payments);
    }

    // An EXECUTED PO payment is money already paid, so each linked shipment
    // gets a matching actual payment record (its pro-rata share) against
    // the "from PO" due line — that is what deducts it from the shipment's
    // balance. Un-executing or deleting the PO payment removes the record.
    // Records are tagged with the PO payment they came from (shadow column
    // PurchaseOrderPaymentId) so they can be told apart from ones typed in
    // by hand; if someone has already recorded a payment by hand against
    // that due line, the automatic record steps aside (no double counting).
    private int? RecLink(ShipmentSupplierPaymentRecord r) => _db.Entry(r).Property<int?>(LinkProp).CurrentValue;

    private async Task SyncPaymentRecordsAsync(int shipmentId, List<PurchaseOrderPayment> payments)
    {
        var usdId = await GetUsdCurrencyIdAsync();
        var dues = await _db.ShipmentPaymentDues
            .Where(d => d.ShipmentId == shipmentId && EF.Property<int?>(d, LinkProp) != null).ToListAsync();
        var records = await _db.ShipmentSupplierPaymentRecords
            .Where(r => r.ShipmentId == shipmentId && r.PaymentDueId != null).ToListAsync();

        foreach (var p in payments)
        {
            var due = dues.FirstOrDefault(d => LinkOf(d) == p.Id);
            var auto = records.FirstOrDefault(r => RecLink(r) == p.Id);
            var handPaid = due is not null && records.Any(r => r.PaymentDueId == due.Id && RecLink(r) is null);
            var want = due is not null && due.Amount > 0 && p.ExecutedDate.HasValue && !handPaid;

            if (!want)
            {
                if (auto is not null) _db.ShipmentSupplierPaymentRecords.Remove(auto);
                continue;
            }

            // Show the shipment's share in the payment's own currency; the USD
            // value is exactly the due line's amount so the line settles to zero.
            var ratio = p.AmountUsd > 0 ? due!.Amount / p.AmountUsd : 0m;
            var value = Math.Round(p.Amount * ratio, 4);
            if (auto is null)
            {
                auto = new ShipmentSupplierPaymentRecord { ShipmentId = shipmentId, PaymentDueId = due!.Id };
                _db.ShipmentSupplierPaymentRecords.Add(auto);
                _db.Entry(auto).Property<int?>(LinkProp).CurrentValue = p.Id;
            }
            auto.PaymentDate = p.ExecutedDate!.Value;
            auto.CurrencyId = p.CurrencyId != 0 ? p.CurrencyId : usdId;
            auto.Value = value;
            auto.ValueUsd = due!.Amount;
        }

        await _db.SaveChangesAsync();
    }

    public async Task SyncOneAsync(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return;

        var linePoIds = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId && li.PurchaseOrderLineItem != null)
            .Select(li => li.PurchaseOrderLineItem!.PurchaseOrderId)
            .Distinct().ToListAsync();
        if (!linePoIds.Contains(shipment.PurchaseOrderId)) linePoIds.Add(shipment.PurchaseOrderId);

        var usdId = await GetUsdCurrencyIdAsync();
        foreach (var poId in linePoIds)
            await SyncShipmentForPoAsync(shipmentId, poId, poId == shipment.PurchaseOrderId, usdId);
    }

    public async Task SyncAllForPoAsync(int purchaseOrderId)
    {
        var viaLines = await _db.ShipmentLineItems
            .Where(li => li.PurchaseOrderLineItem != null && li.PurchaseOrderLineItem.PurchaseOrderId == purchaseOrderId)
            .Select(li => li.ShipmentId).Distinct().ToListAsync();
        var primary = await _db.Shipments.Where(s => s.PurchaseOrderId == purchaseOrderId).Select(s => s.Id).ToListAsync();
        var usdId = await GetUsdCurrencyIdAsync();

        foreach (var id in viaLines.Union(primary))
        {
            var isPrimary = primary.Contains(id);
            await SyncShipmentForPoAsync(id, purchaseOrderId, isPrimary, usdId);
        }
    }

    // ---- Delete support -------------------------------------------------

    public IQueryable<ShipmentSupplierPaymentRecord> AutoRecordsOfPayment(int paymentId) =>
        _db.ShipmentSupplierPaymentRecords.Where(r => EF.Property<int?>(r, LinkProp) == paymentId);

    public IQueryable<ShipmentPaymentDue> DuesOfPayment(int paymentId) =>
        _db.ShipmentPaymentDues.Where(d => EF.Property<int?>(d, LinkProp) == paymentId);
}
