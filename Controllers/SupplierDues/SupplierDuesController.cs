using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.SupplierDues;

public record SupplierDueRow(
    int ShipmentId, string BusinessUnit, string SupplierName, string PoNumber, string? SupplierInvoiceNo,
    string BlAwbNo, DateOnly? Sob, string? PaymentTerm, decimal? InvoiceValue, string? InvoiceCurrency,
    decimal UnpaidBalance, decimal TotalValueUsd, decimal TotalUnpaidUsd);

[ApiController]
[Authorize]
[Route("api/supplier-dues")]
public class SupplierDuesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SupplierDuesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDueRow>>> GetOpen()
    {
        var payments = await _db.ShipmentSupplierPayments
            .Where(p => (p.BalanceUsd ?? 0) > 0)
            .Include(p => p.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(po => po!.BusinessUnit)
            .Include(p => p.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(po => po!.Supplier)
            .Include(p => p.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(po => po!.SupplierPaymentTerm)
            .Include(p => p.InvoiceCurrency)
            .ToListAsync();

        var rows = payments.Select(p =>
        {
            var shipment = p.Shipment!;
            var po = shipment.PurchaseOrder!;
            var invoiceValueUsd = p.InvoiceValueUsd ?? 0;
            var unpaidUsd = p.BalanceUsd ?? 0;
            // Unpaid Balance shown in the invoice's own currency, back-derived
            // from the USD balance using the same rate implied by the invoice.
            var rate = (p.InvoiceValue.HasValue && invoiceValueUsd > 0) ? p.InvoiceValue.Value / invoiceValueUsd : 1m;
            var unpaidInInvoiceCurrency = unpaidUsd * rate;

            return new SupplierDueRow(
                shipment.Id, po.BusinessUnit!.Name, po.Supplier!.Name, po.PoNumber, p.SupplierInvoiceNo,
                shipment.BlAwbNo, shipment.SobActualDate, po.SupplierPaymentTerm!.Name,
                p.InvoiceValue, p.InvoiceCurrency?.Code, unpaidInInvoiceCurrency, invoiceValueUsd, unpaidUsd);
        })
        .OrderBy(r => r.SupplierName)
        .ToList();

        return Ok(rows);
    }
}
