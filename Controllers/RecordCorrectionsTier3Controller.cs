using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;
using Changes = ShippingPortal.Api.Controllers.RecordCorrectionsController.Changes;

namespace ShippingPortal.Api.Controllers;

public record LineCorrectionRow(
    int LineId, string Category, string Model, decimal Qty, LookupRef Uom, decimal UnitPrice, LookupRef Currency,
    decimal Total, decimal TotalUsd, decimal ShippedQty, int ShipmentCount);

public record LinesCorrectionResponse(
    int Id, string PoNumber, string Status, bool MoneyLocked, string? LockReason, int PoPaymentCount, List<LineCorrectionRow> Lines);

public record LineCorrectionInput(int LineId, decimal Qty, decimal UnitPrice, int CurrencyId, int UnitOfMeasureId);
public record LinesCorrectionRequest(List<LineCorrectionInput>? Lines, string? Reason);

public record ShipLineRow(
    int LineId, int PurchaseOrderLineItemId, string PoNumber, string Category, string Model, string Uom, decimal OrderedQty,
    decimal QtyInBl, decimal OtherShipmentsQty, decimal MinQty, decimal MaxQty, decimal UnitPrice, string Currency, decimal ItemSubtotal, string? HsCode);
public record ShipAvailableLine(
    int PurchaseOrderLineItemId, string PoNumber, string Category, string Model, string Uom, decimal OrderedQty, decimal RemainingQty,
    decimal UnitPrice, string Currency);
public record ShipmentLinesResponse(
    int Id, string BlAwbNo, string Status, bool Locked, string? LockReason, List<string> ConfirmedSections,
    List<ShipLineRow> Lines, List<ShipAvailableLine> Available);
public record ShipLineUpdate(int LineId, decimal QtyInBl);
public record ShipLineAddition(int PurchaseOrderLineItemId, decimal QtyInBl, string? HsCode);
public record ShipmentLinesRequest(List<ShipLineUpdate>? Updates, List<ShipLineAddition>? Additions, string? Reason);

public record ChainRow(int Id, int SequenceOrder, LookupRef Partner);
public record ChainResponse(int Id, string PoNumber, List<ChainRow> Chain, int TpEntryCount, int ErpInfoCount, int ShipmentCount, bool SharedWithOtherPos);
public record ChainRequest(List<int>? PartnerIds, string? Reason, bool Confirm);
public record ChainSaveResult(int Changed, bool NeedsConfirm, int TpRows, int ErpRows);

// Phase 3 of "correct an existing PO": the MONEY side - line quantity / unit price /
// currency / UoM, and the offshore chain. SuperUser only. Every change is written to
// DataChangeLogs with a mandatory reason (same table / History view as Phases 1-2).
//
// Rules (agreed with Mohammed):
//  * Qty / unit price / currency are refused once ACTUAL supplier payments exist on any
//    shipment of the PO (UoM alone is always allowed).
//  * Qty can't go below what is already shipped on that line.
//  * Totals are recomputed by the server (Total = Qty x Price; TotalUsd at today's FX, as
//    at creation); shipment line subtotals are re-priced; planned PO payments / shipment
//    payment dues are re-synced; total PO payments can't exceed 100% of the new value.
//  * Offshore chain: replaced as a whole. Transfer Pricing entries of the PO (and the
//    TP section lock) are deleted so TP is redone, and ERP info of changed positions is
//    deleted - after an explicit confirmation showing the row counts.
[ApiController]
[Authorize(Roles = AppRoles.SuperUser)]
[Route("api/corrections")]
public class RecordCorrectionsTier3Controller : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly FxRateService _fx;
    private readonly PoAdvancePaymentService _svc;

    public RecordCorrectionsTier3Controller(ShippingPortalDbContext db, FxRateService fx, PoAdvancePaymentService svc)
    { _db = db; _fx = fx; _svc = svc; }

    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<string> CurrentUserNameAsync(string userId)
    {
        var name = await _db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(name) ? (User.Identity?.Name ?? "") : name;
    }

    private async Task<List<int>> ShipmentIdsOfPoAsync(int poId)
    {
        var viaLines = await _db.ShipmentLineItems
            .Where(li => li.PurchaseOrderLineItem != null && li.PurchaseOrderLineItem.PurchaseOrderId == poId)
            .Select(li => li.ShipmentId).Distinct().ToListAsync();
        var viaLink = await _db.ShipmentPurchaseOrders.Where(x => x.PurchaseOrderId == poId).Select(x => x.ShipmentId).ToListAsync();
        var primary = await _db.Shipments.Where(x => x.PurchaseOrderId == poId).Select(x => x.Id).ToListAsync();
        return viaLines.Union(viaLink).Union(primary).Distinct().ToList();
    }

    // ================= Lines =================
    [HttpGet("orders/{id:int}/lines")]
    public async Task<ActionResult<LinesCorrectionResponse>> GetLines(int id)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        var lines = await _db.PurchaseOrderLineItems
            .Include(l => l.ProductCategory).Include(l => l.ModelProduct).Include(l => l.UnitOfMeasure).Include(l => l.Currency)
            .Where(l => l.PurchaseOrderId == id).OrderBy(l => l.Id).ToListAsync();
        var lineIds = lines.Select(l => l.Id).ToList();
        var shipped = await _db.ShipmentLineItems
            .Where(sl => lineIds.Contains(sl.PurchaseOrderLineItemId))
            .GroupBy(sl => sl.PurchaseOrderLineItemId)
            .Select(g => new { LineId = g.Key, Qty = g.Sum(x => x.QtyInBl), Count = g.Select(x => x.ShipmentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.LineId);

        var (locked, reason) = await MoneyLockAsync(id);
        var paymentCount = await _db.PurchaseOrderPayments.CountAsync(x => x.PurchaseOrderId == id);

        var rows = lines.Select(l => new LineCorrectionRow(
            l.Id, l.ProductCategory?.Name ?? "", l.ModelProduct?.Name ?? "", l.Qty,
            new LookupRef(l.UnitOfMeasureId, l.UnitOfMeasure?.Code ?? ""), l.UnitPrice,
            new LookupRef(l.CurrencyId, l.Currency?.Code ?? ""), l.Total, l.TotalUsd,
            shipped.TryGetValue(l.Id, out var s) ? s.Qty : 0m, shipped.TryGetValue(l.Id, out var s2) ? s2.Count : 0)).ToList();

        return new LinesCorrectionResponse(po.Id, po.PoNumber, po.Status.ToString(), locked, reason, paymentCount, rows);
    }

    // Actual supplier payments recorded on any shipment of this PO (this includes the records
    // auto-created from executed PO payments) freeze the money side.
    private async Task<(bool Locked, string? Reason)> MoneyLockAsync(int poId)
    {
        var shipmentIds = await ShipmentIdsOfPoAsync(poId);
        if (shipmentIds.Count == 0) return (false, null);
        var n = await _db.ShipmentSupplierPaymentRecords.CountAsync(r => shipmentIds.Contains(r.ShipmentId));
        return n > 0
            ? (true, $"{n} actual supplier payment record(s) exist on this order's shipment(s). Quantity, price and currency can't be changed once money has been paid; only the unit of measure can. Remove the payment record(s) first if a correction is really needed.")
            : (false, null);
    }

    [HttpPut("orders/{id:int}/lines")]
    public async Task<IActionResult> SaveLines(int id, LinesCorrectionRequest req)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        var lines = await _db.PurchaseOrderLineItems
            .Include(l => l.ModelProduct).Include(l => l.UnitOfMeasure).Include(l => l.Currency)
            .Where(l => l.PurchaseOrderId == id).ToListAsync();
        var inputs = req.Lines ?? new List<LineCorrectionInput>();
        if (inputs.Any(i => lines.All(l => l.Id != i.LineId)))
            return BadRequest(new { message = "One of the lines doesn't belong to this order." });
        if (inputs.Any(i => i.Qty <= 0)) return BadRequest(new { message = "Quantity must be greater than zero." });
        if (inputs.Any(i => i.UnitPrice < 0)) return BadRequest(new { message = "Unit price cannot be negative." });
        if (inputs.GroupBy(i => i.LineId).Any(g => g.Count() > 1)) return BadRequest(new { message = "Duplicate line in the request." });

        // Work out what actually changes before touching anything.
        var moneyChanged = new List<(PurchaseOrderLineItem Line, LineCorrectionInput In)>();
        var uomOnly = new List<(PurchaseOrderLineItem Line, LineCorrectionInput In)>();
        foreach (var i in inputs)
        {
            var l = lines.First(x => x.Id == i.LineId);
            var money = i.Qty != l.Qty || i.UnitPrice != l.UnitPrice || i.CurrencyId != l.CurrencyId;
            if (money) moneyChanged.Add((l, i));
            else if (i.UnitOfMeasureId != l.UnitOfMeasureId) uomOnly.Add((l, i));
        }
        if (moneyChanged.Count == 0 && uomOnly.Count == 0) return Ok(new { changed = 0 });

        var reason = Norm(req.Reason);
        if (reason is null || reason.Length < 5)
            return BadRequest(new { message = "Please give a short reason for the correction (at least 5 characters)." });

        if (moneyChanged.Count > 0)
        {
            var (locked, lockReason) = await MoneyLockAsync(id);
            if (locked) return StatusCode(409, new { message = lockReason });
        }

        // Currency / UoM must exist.
        var currencyIds = moneyChanged.Select(mc => mc.In.CurrencyId).Concat(uomOnly.Select(u => u.Line.CurrencyId)).Distinct().ToList();
        var knownCurrencies = await _db.Currencies.Where(cur => currencyIds.Contains(cur.Id)).Select(cur => cur.Id).ToListAsync();
        if (currencyIds.Any(cid => !knownCurrencies.Contains(cid))) return BadRequest(new { message = "Invalid currency selection." });

        // Qty can't drop below what is already shipped on the line.
        var changedIds = moneyChanged.Select(mc => mc.Line.Id).ToList();
        var shipped = await _db.ShipmentLineItems
            .Where(sl => changedIds.Contains(sl.PurchaseOrderLineItemId))
            .GroupBy(sl => sl.PurchaseOrderLineItemId)
            .Select(g => new { LineId = g.Key, Qty = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.LineId, x => x.Qty);
        foreach (var (line, inp) in moneyChanged)
            if (shipped.TryGetValue(line.Id, out var q) && inp.Qty < q)
                return BadRequest(new { message = $"'{line.ModelProduct?.Name}': quantity {inp.Qty:0.####} is below the {q:0.####} already shipped on shipments." });

        // PO payments (by value) can't exceed 100% of the corrected order value.
        if (moneyChanged.Count > 0)
        {
            var finalLines = lines.Select(l =>
            {
                var hit = moneyChanged.FirstOrDefault(x => x.Line.Id == l.Id);
                return hit.Line is null ? (l.Qty, l.UnitPrice, l.CurrencyId) : (hit.In.Qty, hit.In.UnitPrice, hit.In.CurrencyId);
            }).ToList();
            var distinctCur = finalLines.Select(f => f.Item3).Distinct().ToList();
            var basis = new PoAdvancePaymentService.PoBasis
            {
                PurchaseOrderId = id,
                DefaultCurrencyId = finalLines.FirstOrDefault().Item3,
                SingleCurrencyId = distinctCur.Count == 1 ? distinctCur[0] : null,
                NativeTotal = finalLines.Sum(f => f.Item1 * f.Item2)
            };
            foreach (var cid in distinctCur) basis.Rates[cid] = await _svc.GetFxRateAsync(cid);
            basis.UsdTotal = finalLines.Sum(f => f.Item1 * f.Item2 / basis.Rates[f.Item3]);
            var payments = await _db.PurchaseOrderPayments.Where(p => p.PurchaseOrderId == id).ToListAsync();
            var pct = payments.Sum(p => PoAdvancePaymentService.PercentOf(p, basis));
            if (pct > 100.01m)
                return BadRequest(new { message = $"The PO's recorded payments would then be {pct:0.##}% of the corrected order value (max 100%). Adjust the PO payments first." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var c = new Changes("PurchaseOrder", po.Id, po.PoNumber, userId, await CurrentUserNameAsync(userId));

        var repriced = 0;
        foreach (var i in inputs)
        {
            var l = lines.First(x => x.Id == i.LineId);
            var model = l.ModelProduct?.Name ?? $"line {l.Id}";
            var tag = "Line " + (model.Length > 40 ? model[..40] : model);
            var oldQty = l.Qty; var oldPrice = l.UnitPrice; var oldCur = l.CurrencyId;

            c.Decimal($"{tag} - Qty", l.Qty, i.Qty, v => l.Qty = v);
            c.Decimal($"{tag} - Unit Price", l.UnitPrice, i.UnitPrice, v => l.UnitPrice = v);
            await c.Lookup($"{tag} - Currency", l.CurrencyId, l.Currency?.Code, i.CurrencyId,
                id2 => _db.Currencies.Where(x => x.Id == id2).Select(x => x.Code).FirstOrDefaultAsync(), v => l.CurrencyId = v!.Value);
            await c.Lookup($"{tag} - UoM", l.UnitOfMeasureId, l.UnitOfMeasure?.Code, i.UnitOfMeasureId,
                id2 => _db.UnitsOfMeasure.Where(x => x.Id == id2).Select(x => x.Code).FirstOrDefaultAsync(), v => l.UnitOfMeasureId = v!.Value);
            if (c.Error is not null) return BadRequest(new { message = c.Error });

            if (l.Qty != oldQty || l.UnitPrice != oldPrice || l.CurrencyId != oldCur)
            {
                l.Total = l.Qty * l.UnitPrice;
                var rate = await _fx.GetRateToUsdAsync(l.CurrencyId);
                l.TotalUsd = rate == 0 ? l.Total : l.Total / rate;

                // Shipment line subtotals follow the PO price (QtyInBl x unit price), as at shipment creation.
                if (l.UnitPrice != oldPrice)
                {
                    var sls = await _db.ShipmentLineItems.Where(sl => sl.PurchaseOrderLineItemId == l.Id).ToListAsync();
                    foreach (var sl in sls) sl.ItemSubtotal = sl.QtyInBl * l.UnitPrice;
                    repriced += sls.Count;
                }
            }
        }

        if (c.Rows.Count == 0) return Ok(new { changed = 0 });

        if (repriced > 0) c.Note("Shipment line subtotals recalculated", $"{repriced} line(s)");
        c.Note("Correction reason", reason.Length > 480 ? reason[..480] : reason);
        po.UpdatedAt = DateTime.UtcNow;
        _db.DataChangeLogs.AddRange(c.Rows);
        await _db.SaveChangesAsync();

        // Planned PO payments / shipment payment-due lines are derived from the PO value and each
        // shipment's share of it - refresh them (executed ones keep their frozen USD).
        if (moneyChanged.Count > 0) await _svc.SyncAllForPoAsync(id);

        return Ok(new { changed = c.Rows.Count(r => r.FieldName != "Correction reason" && !r.FieldName.StartsWith("Shipment line subtotals")) });
    }

    // ================= Offshore chain =================
    [HttpGet("orders/{id:int}/chain")]
    public async Task<ActionResult<ChainResponse>> GetChain(int id)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        var chain = await _db.PurchaseOrderOffshorePartners.Include(o => o.BusinessPartner)
            .Where(o => o.PurchaseOrderId == id).OrderBy(o => o.SequenceOrder).ToListAsync();
        var rowIds = chain.Select(c => c.Id).ToList();
        var tp = await _db.TransferPricingEntries.CountAsync(t => rowIds.Contains(t.PurchaseOrderOffshorePartnerId));
        var erp = await _db.ShipmentOffshoreErpInfos.CountAsync(e => rowIds.Contains(e.PurchaseOrderOffshorePartnerId));
        var shipmentIds = await ShipmentIdsOfPoAsync(id);
        var shared = await _db.ShipmentPurchaseOrders.AnyAsync(x => shipmentIds.Contains(x.ShipmentId) && x.PurchaseOrderId != id);

        return new ChainResponse(po.Id, po.PoNumber,
            chain.Select(c => new ChainRow(c.Id, c.SequenceOrder, new LookupRef(c.BusinessPartnerId, c.BusinessPartner?.Name ?? ""))).ToList(),
            tp, erp, shipmentIds.Count, shared);
    }

    [HttpPut("orders/{id:int}/chain")]
    public async Task<ActionResult<ChainSaveResult>> SaveChain(int id, ChainRequest req)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        var newIds = req.PartnerIds ?? new List<int>();
        var partners = await _db.BusinessPartners.Where(b => newIds.Contains(b.Id) && b.IsOffshoreEntity).ToDictionaryAsync(b => b.Id, b => b.Name);
        if (newIds.Any(x => !partners.ContainsKey(x)))
            return BadRequest(new { message = "Every chain entry must be an offshore entity from the partner list." });

        var current = await _db.PurchaseOrderOffshorePartners.Include(o => o.BusinessPartner)
            .Where(o => o.PurchaseOrderId == id).OrderBy(o => o.SequenceOrder).ToListAsync();
        var bySeq = current.ToDictionary(o => o.SequenceOrder);

        var changedRows = current.Where(o => !(o.SequenceOrder >= 1 && o.SequenceOrder <= newIds.Count && newIds[o.SequenceOrder - 1] == o.BusinessPartnerId)).ToList();
        var addSeqs = Enumerable.Range(1, newIds.Count).Where(s => !bySeq.ContainsKey(s)).ToList();
        if (changedRows.Count == 0 && addSeqs.Count == 0) return new ChainSaveResult(0, false, 0, 0);

        var reason = Norm(req.Reason);
        if (reason is null || reason.Length < 5)
            return BadRequest(new { message = "Please give a short reason for the correction (at least 5 characters)." });

        // A shipment combining several POs must keep one offshore chain.
        var shipmentIds = await ShipmentIdsOfPoAsync(id);
        var otherPoIds = await _db.ShipmentPurchaseOrders
            .Where(x => shipmentIds.Contains(x.ShipmentId) && x.PurchaseOrderId != id)
            .Select(x => x.PurchaseOrderId).Distinct().ToListAsync();
        if (otherPoIds.Count > 0)
        {
            var others = await _db.PurchaseOrderOffshorePartners.Where(o => otherPoIds.Contains(o.PurchaseOrderId))
                .OrderBy(o => o.SequenceOrder).ToListAsync();
            foreach (var g in others.GroupBy(o => o.PurchaseOrderId))
                if (!g.Select(o => o.BusinessPartnerId).SequenceEqual(newIds))
                {
                    var no = await _db.PurchaseOrders.Where(p => p.Id == g.Key).Select(p => p.PoNumber).FirstAsync();
                    return BadRequest(new { message = $"This PO shares a shipment with PO {no}, and POs in one shipment must have the same offshore chain. Change them together (apply the same chain to each PO)." });
                }
        }

        var allRowIds = current.Select(o => o.Id).ToList();
        var changedIds = changedRows.Select(o => o.Id).ToList();
        var tpEntries = await _db.TransferPricingEntries.Where(t => allRowIds.Contains(t.PurchaseOrderOffshorePartnerId)).ToListAsync();
        var erpRows = await _db.ShipmentOffshoreErpInfos.Where(e => changedIds.Contains(e.PurchaseOrderOffshorePartnerId)).ToListAsync();

        if ((tpEntries.Count + erpRows.Count) > 0 && !req.Confirm)
            return new ChainSaveResult(0, true, tpEntries.Count, erpRows.Count);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var c = new Changes("PurchaseOrder", po.Id, po.PoNumber, userId, await CurrentUserNameAsync(userId));
        string Chain(IEnumerable<string> names) { var l = names.ToList(); return l.Count == 0 ? "(none)" : string.Join(" > ", l); }
        c.Text("Offshore chain", Chain(current.Select(o => o.BusinessPartner?.Name ?? "?")), Chain(newIds.Select(x => partners[x])), _ => { });

        if (tpEntries.Count > 0)
        {
            _db.TransferPricingEntries.RemoveRange(tpEntries);
            c.Note("Transfer Pricing entries deleted", $"{tpEntries.Count} row(s) - TP must be redone");
            // The TP section is "confirmed/locked" per shipment; unlock it so TP can be redone.
            var locks = await _db.SectionLocks
                .Where(l => l.EntityType == "Shipment" && l.SectionKey == "transferPricing" && shipmentIds.Contains(l.EntityId)).ToListAsync();
            _db.SectionLocks.RemoveRange(locks);
        }
        if (erpRows.Count > 0)
        {
            _db.ShipmentOffshoreErpInfos.RemoveRange(erpRows);
            c.Note("Offshore ERP info deleted", $"{erpRows.Count} row(s)");
        }

        foreach (var row in changedRows)
        {
            if (row.SequenceOrder <= newIds.Count && row.SequenceOrder >= 1) row.BusinessPartnerId = newIds[row.SequenceOrder - 1];
            else _db.PurchaseOrderOffshorePartners.Remove(row);
        }
        foreach (var seq in addSeqs)
            _db.PurchaseOrderOffshorePartners.Add(new PurchaseOrderOffshorePartner { PurchaseOrderId = id, BusinessPartnerId = newIds[seq - 1], SequenceOrder = seq });

        c.Note("Correction reason", reason.Length > 480 ? reason[..480] : reason);
        po.UpdatedAt = DateTime.UtcNow;
        _db.DataChangeLogs.AddRange(c.Rows);
        await _db.SaveChangesAsync();
        return new ChainSaveResult(1, false, tpEntries.Count, erpRows.Count);
    }

    // ================= Shipment lines (quantity in BL / add missing items) =================
    private async Task<(bool Locked, string? Reason)> ShipmentPaidLockAsync(int shipmentId)
    {
        var n = await _db.ShipmentSupplierPaymentRecords.CountAsync(r => r.ShipmentId == shipmentId);
        return n > 0
            ? (true, $"{n} actual supplier payment record(s) exist on this shipment. Quantities can't be changed and items can't be added once money has been paid against it.")
            : (false, null);
    }

    // The smallest quantity a shipment line may be reduced to: whatever has already been allocated to warehouses
    // or drawn down by withdrawals from this line.
    private async Task<Dictionary<int, decimal>> LineFloorsAsync(List<int> shipmentLineIds)
    {
        var alloc = await _db.WarehouseAllocations
            .Where(a => a.ShipmentLineItemId != null && shipmentLineIds.Contains(a.ShipmentLineItemId.Value))
            .GroupBy(a => a.ShipmentLineItemId!.Value).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Qty) }).ToListAsync();
        var wd = await _db.WithdrawalLineItems
            .Where(w => shipmentLineIds.Contains(w.DepositShipmentLineItemId))
            .GroupBy(w => w.DepositShipmentLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Qty) }).ToListAsync();
        var r3 = await _db.ClearanceRoute3Withdrawals
            .Where(w => shipmentLineIds.Contains(w.DepositShipmentLineItemId))
            .GroupBy(w => w.DepositShipmentLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Qty) }).ToListAsync();

        var floors = new Dictionary<int, decimal>();
        foreach (var x in alloc.Concat(wd).Concat(r3))
            floors[x.Id] = Math.Max(floors.GetValueOrDefault(x.Id), x.Qty);
        return floors;
    }

    [HttpGet("shipments/{id:int}/lines")]
    public async Task<ActionResult<ShipmentLinesResponse>> GetShipmentLines(int id)
    {
        var sh = await _db.Shipments.FirstOrDefaultAsync(x => x.Id == id);
        if (sh is null) return NotFound();

        var lines = await _db.ShipmentLineItems
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.PurchaseOrder)
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.ProductCategory)
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.ModelProduct)
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.UnitOfMeasure)
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.Currency)
            .Where(l => l.ShipmentId == id).OrderBy(l => l.Id).ToListAsync();

        var poLineIds = lines.Select(l => l.PurchaseOrderLineItemId).ToList();
        var othersByPoLine = await _db.ShipmentLineItems
            .Where(l => poLineIds.Contains(l.PurchaseOrderLineItemId) && l.ShipmentId != id && l.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(l => l.PurchaseOrderLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);
        var floors = await LineFloorsAsync(lines.Select(l => l.Id).ToList());

        var rows = lines.Select(l =>
        {
            var p = l.PurchaseOrderLineItem!;
            var others = othersByPoLine.GetValueOrDefault(p.Id, 0m);
            return new ShipLineRow(
                l.Id, p.Id, p.PurchaseOrder?.PoNumber ?? "", p.ProductCategory?.Name ?? "", p.ModelProduct?.Name ?? "", p.UnitOfMeasure?.Code ?? "",
                p.Qty, l.QtyInBl, others, floors.GetValueOrDefault(l.Id, 0m), p.Qty - others, p.UnitPrice, p.Currency?.Code ?? "", l.ItemSubtotal, l.HsCode);
        }).ToList();

        // Lines of this shipment's own PO(s) that are not on it yet and still have quantity left.
        var poIds = (await ShipmentIdsOfPoAsyncForShipment(id));
        var existingPoLineIds = poLineIds.ToHashSet();
        var candidates = await _db.PurchaseOrderLineItems
            .Include(p => p.PurchaseOrder).Include(p => p.ProductCategory).Include(p => p.ModelProduct)
            .Include(p => p.UnitOfMeasure).Include(p => p.Currency)
            .Where(p => poIds.Contains(p.PurchaseOrderId) && !existingPoLineIds.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync();
        var candIds = candidates.Select(p => p.Id).ToList();
        var shippedElsewhere = await _db.ShipmentLineItems
            .Where(l => candIds.Contains(l.PurchaseOrderLineItemId) && l.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(l => l.PurchaseOrderLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);
        var available = candidates
            .Select(p => new ShipAvailableLine(p.Id, p.PurchaseOrder?.PoNumber ?? "", p.ProductCategory?.Name ?? "", p.ModelProduct?.Name ?? "",
                p.UnitOfMeasure?.Code ?? "", p.Qty, p.Qty - shippedElsewhere.GetValueOrDefault(p.Id, 0m), p.UnitPrice, p.Currency?.Code ?? ""))
            .Where(a => a.RemainingQty > 0).ToList();

        var (locked, reason) = await ShipmentPaidLockAsync(id);
        var sections = await _db.SectionLocks.Where(k => k.EntityType == "Shipment" && k.EntityId == id).Select(k => k.SectionKey).ToListAsync();

        return new ShipmentLinesResponse(sh.Id, sh.BlAwbNo, sh.Status.ToString(), locked, reason, sections, rows, available);
    }

    private async Task<List<int>> ShipmentIdsOfPoAsyncForShipment(int shipmentId)
    {
        var viaLink = await _db.ShipmentPurchaseOrders.Where(x => x.ShipmentId == shipmentId).Select(x => x.PurchaseOrderId).ToListAsync();
        var primary = await _db.Shipments.Where(x => x.Id == shipmentId).Select(x => x.PurchaseOrderId).ToListAsync();
        return viaLink.Union(primary).Distinct().ToList();
    }

    [HttpPut("shipments/{id:int}/lines")]
    public async Task<IActionResult> SaveShipmentLines(int id, ShipmentLinesRequest req)
    {
        var sh = await _db.Shipments.FirstOrDefaultAsync(x => x.Id == id);
        if (sh is null) return NotFound();
        if (sh.Status == ShipmentStatus.Cancelled) return BadRequest(new { message = "This shipment is cancelled." });

        var updates = (req.Updates ?? new List<ShipLineUpdate>()).ToList();
        var additions = (req.Additions ?? new List<ShipLineAddition>()).ToList();
        if (updates.Any(u => u.QtyInBl <= 0) || additions.Any(a => a.QtyInBl <= 0))
            return BadRequest(new { message = "Quantities must be greater than zero." });
        if (additions.GroupBy(a => a.PurchaseOrderLineItemId).Any(g => g.Count() > 1))
            return BadRequest(new { message = "The same item was added twice." });

        var lines = await _db.ShipmentLineItems
            .Include(l => l.PurchaseOrderLineItem!).ThenInclude(p => p.ModelProduct)
            .Where(l => l.ShipmentId == id).ToListAsync();

        // Only real changes count.
        var qtyChanges = updates.Where(u => lines.Any(l => l.Id == u.LineId && l.QtyInBl != u.QtyInBl)).ToList();
        if (updates.Any(u => lines.All(l => l.Id != u.LineId)))
            return BadRequest(new { message = "One of the lines doesn't belong to this shipment." });
        if (qtyChanges.Count == 0 && additions.Count == 0) return Ok(new { changed = 0 });

        var reason = Norm(req.Reason);
        if (reason is null || reason.Length < 5)
            return BadRequest(new { message = "Please give a short reason for the correction (at least 5 characters)." });

        var (locked, lockReason) = await ShipmentPaidLockAsync(id);
        if (locked) return StatusCode(409, new { message = lockReason });

        // ---- quantity changes on existing lines ----
        var poLineIds = lines.Select(l => l.PurchaseOrderLineItemId).ToList();
        var othersByPoLine = await _db.ShipmentLineItems
            .Where(l => poLineIds.Contains(l.PurchaseOrderLineItemId) && l.ShipmentId != id && l.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(l => l.PurchaseOrderLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);
        var floors = await LineFloorsAsync(qtyChanges.Select(u => u.LineId).ToList());

        foreach (var u in qtyChanges)
        {
            var l = lines.First(x => x.Id == u.LineId);
            var p = l.PurchaseOrderLineItem!;
            var max = p.Qty - othersByPoLine.GetValueOrDefault(p.Id, 0m);
            if (u.QtyInBl > max)
                return BadRequest(new { message = $"'{p.ModelProduct?.Name}': quantity {u.QtyInBl:0.####} exceeds what is left on the order ({max:0.####})." });
            var floor = floors.GetValueOrDefault(l.Id, 0m);
            if (u.QtyInBl < floor)
                return BadRequest(new { message = $"'{p.ModelProduct?.Name}': quantity {u.QtyInBl:0.####} is below the {floor:0.####} already allocated to warehouses / withdrawn." });
        }

        // ---- additions ----
        var allowedPoIds = await ShipmentIdsOfPoAsyncForShipment(id);
        var addIds = additions.Select(a => a.PurchaseOrderLineItemId).ToList();
        var addLines = await _db.PurchaseOrderLineItems
            .Include(p => p.PurchaseOrder).Include(p => p.ModelProduct)
            .Where(p => addIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var shippedByAdd = await _db.ShipmentLineItems
            .Where(l => addIds.Contains(l.PurchaseOrderLineItemId) && l.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(l => l.PurchaseOrderLineItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty);

        foreach (var a in additions)
        {
            if (!addLines.TryGetValue(a.PurchaseOrderLineItemId, out var pl))
                return BadRequest(new { message = "An added item was not found." });
            if (!allowedPoIds.Contains(pl.PurchaseOrderId))
                return BadRequest(new { message = $"'{pl.ModelProduct?.Name}' belongs to a PO that is not part of this shipment." });
            if (lines.Any(l => l.PurchaseOrderLineItemId == pl.Id))
                return BadRequest(new { message = $"'{pl.ModelProduct?.Name}' is already on this shipment - change its quantity instead." });
            if (pl.PurchaseOrder?.Status != OrderStatus.Confirmed)
                return BadRequest(new { message = $"PO {pl.PurchaseOrder?.PoNumber} is not confirmed." });
            var remaining = pl.Qty - shippedByAdd.GetValueOrDefault(pl.Id, 0m);
            if (a.QtyInBl > remaining)
                return BadRequest(new { message = $"'{pl.ModelProduct?.Name}': quantity {a.QtyInBl:0.####} exceeds what is left on the order ({remaining:0.####})." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var chg = new Changes("Shipment", sh.Id, sh.BlAwbNo, userId, await CurrentUserNameAsync(userId));

        foreach (var u in qtyChanges)
        {
            var l = lines.First(x => x.Id == u.LineId);
            var p = l.PurchaseOrderLineItem!;
            var model = p.ModelProduct?.Name ?? $"line {l.Id}";
            chg.Decimal($"Line {(model.Length > 40 ? model[..40] : model)} - Qty in BL", l.QtyInBl, u.QtyInBl, v => l.QtyInBl = v);
            l.ItemSubtotal = l.QtyInBl * p.UnitPrice;
        }
        foreach (var a in additions)
        {
            var pl = addLines[a.PurchaseOrderLineItemId];
            var model = pl.ModelProduct?.Name ?? $"line {pl.Id}";
            _db.ShipmentLineItems.Add(new ShipmentLineItem
            {
                ShipmentId = id, PurchaseOrderLineItemId = pl.Id, QtyInBl = a.QtyInBl, ItemSubtotal = a.QtyInBl * pl.UnitPrice, HsCode = Norm(a.HsCode)
            });
            chg.Note($"Line added: {(model.Length > 50 ? model[..50] : model)}", $"Qty in BL {a.QtyInBl.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        chg.Note("Correction reason", reason.Length > 480 ? reason[..480] : reason);
        sh.UpdatedAt = DateTime.UtcNow;
        _db.DataChangeLogs.AddRange(chg.Rows);
        await _db.SaveChangesAsync();

        // Shipment value changed -> refresh the shipment's PO-payment due lines (shares of each PO payment).
        await _svc.SyncOneAsync(id);

        return Ok(new { changed = qtyChanges.Count + additions.Count });
    }
}
