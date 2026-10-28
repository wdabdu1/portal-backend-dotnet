using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Services;
using System.Security.Claims;

namespace ShippingPortal.Api.Controllers.Orders;

public record PoPaymentRequest(string? Description, decimal Amount, int CurrencyId, DateOnly? PlannedDate, DateOnly? ExecutedDate, string? Note);

public record PoPaymentItem(
    int Id, string Description, decimal Amount, int CurrencyId, string CurrencyCode, decimal AmountUsd,
    decimal Percent, DateOnly? PlannedDate, DateOnly? ExecutedDate, string? Note);

public record PoPaymentsSummary(
    int DefaultCurrencyId, string DefaultCurrencyCode, bool IsMixedCurrency,
    decimal PoTotalNative, decimal PoTotalUsd,
    decimal TotalPercent, decimal ExecutedPercent, decimal RemainingPercent,
    decimal TotalAmountInPoCurrency, decimal ExecutedAmountInPoCurrency, decimal RemainingAmountInPoCurrency,
    List<PoPaymentItem> Items);

// Several payments per PO, entered by value (the % is derived). Each
// change re-syncs every linked shipment's Payment Due Schedule.
[ApiController]
[Authorize(Roles = AppRoles.OrdersShipmentsViewers)]
[Route("api/purchase-orders/{poId:int}/payments")]
public class PurchaseOrderPaymentsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly PoAdvancePaymentService _svc;

    public PurchaseOrderPaymentsController(ShippingPortalDbContext db, PoAdvancePaymentService svc)
    {
        _db = db;
        _svc = svc;
    }

    [HttpGet]
    public async Task<ActionResult<PoPaymentsSummary>> Get(int poId, [FromServices] BuAccessService buAccess)
    {
        var po = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poId);
        if (po is null) return NotFound();
        if (!buAccess.SeesAllBus(User) && !buAccess.CanSeeBusinessUnit(User, po.BusinessUnitId)) return Forbid();
        return Ok(await BuildSummaryAsync(poId));
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<ActionResult<PoPaymentsSummary>> Create(int poId, PoPaymentRequest req)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        if (po is null) return NotFound();
        if (!HasWriteAccess(po.BusinessUnitId)) return Forbid();

        var existing = await _db.PurchaseOrderPayments.Where(p => p.PurchaseOrderId == poId).ToListAsync();
        var payment = new PurchaseOrderPayment { PurchaseOrderId = poId, CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };

        var error = await ApplyAsync(payment, req, existing, defaultName: existing.Count == 0 ? "Advance" : $"Payment {existing.Count + 1}");
        if (error is not null) return BadRequest(new { message = error });

        _db.PurchaseOrderPayments.Add(payment);
        await _db.SaveChangesAsync();
        await _svc.SyncAllForPoAsync(poId);
        return Ok(await BuildSummaryAsync(poId));
    }

    [HttpPut("{paymentId:int}")]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<ActionResult<PoPaymentsSummary>> Update(int poId, int paymentId, PoPaymentRequest req)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        if (po is null) return NotFound();
        if (!HasWriteAccess(po.BusinessUnitId)) return Forbid();

        var all = await _db.PurchaseOrderPayments.Where(p => p.PurchaseOrderId == poId).ToListAsync();
        var payment = all.FirstOrDefault(p => p.Id == paymentId);
        if (payment is null) return NotFound();

        var error = await ApplyAsync(payment, req, all.Where(p => p.Id != paymentId).ToList(), defaultName: payment.Description);
        if (error is not null) return BadRequest(new { message = error });

        await _db.SaveChangesAsync();
        await _svc.SyncAllForPoAsync(poId);
        return Ok(await BuildSummaryAsync(poId));
    }

    [HttpDelete("{paymentId:int}")]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<ActionResult<PoPaymentsSummary>> Delete(int poId, int paymentId)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == poId);
        if (po is null) return NotFound();
        if (!HasWriteAccess(po.BusinessUnitId)) return Forbid();

        var payment = await _db.PurchaseOrderPayments.FirstOrDefaultAsync(p => p.Id == paymentId && p.PurchaseOrderId == poId);
        if (payment is null) return NotFound();

        var dues = await _svc.DuesOfPayment(paymentId).ToListAsync();
        var autoRecords = await _svc.AutoRecordsOfPayment(paymentId).ToListAsync();
        var dueIds = dues.Select(d => d.Id).ToList();
        // Records created automatically from this payment go with it; records
        // someone typed in by hand against these lines block the delete.
        var autoIds = autoRecords.Select(r => r.Id).ToHashSet();
        if (dueIds.Count > 0 && await _db.ShipmentSupplierPaymentRecords.AnyAsync(r => r.PaymentDueId != null && dueIds.Contains(r.PaymentDueId.Value) && !autoIds.Contains(r.Id)))
            return BadRequest(new { message = "This payment can't be deleted: actual payments have already been recorded by hand against it on one or more shipments. Remove those shipment payment records first." });

        _db.ShipmentSupplierPaymentRecords.RemoveRange(autoRecords);
        _db.ShipmentPaymentDues.RemoveRange(dues);
        _db.PurchaseOrderPayments.Remove(payment);
        await _db.SaveChangesAsync();
        return Ok(await BuildSummaryAsync(poId));
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<string?> ApplyAsync(PurchaseOrderPayment payment, PoPaymentRequest req, List<PurchaseOrderPayment> others, string defaultName)
    {
        if (req.Amount <= 0) return "Amount must be greater than zero.";
        if (!req.PlannedDate.HasValue && !req.ExecutedDate.HasValue) return "Enter a planned date (and the actual execution date once paid).";
        if (!await _db.Currencies.AnyAsync(c => c.Id == req.CurrencyId)) return "Choose a currency.";

        var name = string.IsNullOrWhiteSpace(req.Description) ? defaultName : req.Description.Trim();
        if (name.Length > 60) return "Description is too long (max 60 characters).";
        if (others.Any(o => string.Equals(o.Description, name, StringComparison.OrdinalIgnoreCase)))
            return $"This order already has a payment called '{name}'. Use a different description.";

        payment.Description = name;
        payment.Amount = Math.Round(req.Amount, 4);
        payment.CurrencyId = req.CurrencyId;
        payment.AmountUsd = Math.Round(payment.Amount / await _svc.GetFxRateAsync(req.CurrencyId), 4);
        payment.PlannedDate = req.PlannedDate;
        payment.ExecutedDate = req.ExecutedDate;
        payment.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        payment.UpdatedAt = DateTime.UtcNow;

        // Total payments can never exceed the order value.
        var basis = await _svc.GetPoBasisAsync(payment.PurchaseOrderId);
        var total = others.Sum(o => PoAdvancePaymentService.PercentOf(o, basis)) + PoAdvancePaymentService.PercentOf(payment, basis);
        if (total > 100.01m)
            return $"Total payments would be {total:0.##}% of the order value. They can't exceed 100%.";
        return null;
    }

    private async Task<PoPaymentsSummary> BuildSummaryAsync(int poId)
    {
        var basis = await _svc.GetPoBasisAsync(poId);
        var payments = await _db.PurchaseOrderPayments.AsNoTracking().Where(p => p.PurchaseOrderId == poId).OrderBy(p => p.PlannedDate).ThenBy(p => p.Id).ToListAsync();
        var currencies = await _db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code);
        string Code(int id) => currencies.TryGetValue(id, out var c) ? c : "";

        var items = payments.Select(p => new PoPaymentItem(
            p.Id, p.Description, p.Amount, p.CurrencyId, Code(p.CurrencyId), p.AmountUsd,
            Math.Round(PoAdvancePaymentService.PercentOf(p, basis), 4), p.PlannedDate, p.ExecutedDate, p.Note)).ToList();

        var totalPct = items.Sum(i => i.Percent);
        var execPct = items.Where(i => i.ExecutedDate.HasValue).Sum(i => i.Percent);
        var remainingPct = Math.Max(0m, 100m - totalPct);

        // Amounts expressed in the PO's own currency (single currency) so the
        // header reads naturally; for a mixed-currency PO this is USD-equivalent
        // of the default currency's share and the UI labels it accordingly.
        var poTotal = basis.SingleCurrencyId.HasValue ? basis.NativeTotal : basis.UsdTotal;
        return new PoPaymentsSummary(
            basis.DefaultCurrencyId, Code(basis.DefaultCurrencyId), !basis.SingleCurrencyId.HasValue,
            basis.NativeTotal, basis.UsdTotal,
            totalPct, execPct, remainingPct,
            Math.Round(poTotal * totalPct / 100m, 2), Math.Round(poTotal * execPct / 100m, 2), Math.Round(poTotal * remainingPct / 100m, 2),
            items);
    }

    // Same rule as the rest of the Orders module: Manager/SuperUser write
    // anywhere; others need ReadWrite on the PO's business unit.
    private bool HasWriteAccess(int businessUnitId)
    {
        if (User.IsInRole(AppRoles.Manager) || User.IsInRole(AppRoles.SuperUser)) return true;
        return User.Claims.Any(c => c.Type == "bu" && c.Value.StartsWith($"{businessUnitId}:") && c.Value.EndsWith(":ReadWrite"));
    }
}
