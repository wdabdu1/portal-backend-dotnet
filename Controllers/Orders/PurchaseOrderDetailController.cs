using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Orders;

public record PoLineItemDetail(
    string ProductCategory, string ProductType, string ModelProduct,
    decimal Qty, string UnitOfMeasure,
    decimal? UnitPrice, string? Currency, decimal? TotalValue);

public record PoOffshorePartnerDetail(int SequenceOrder, string Name, bool IsLast);

public record PurchaseOrderDetailResponse(
    int Id, string PoNumber, string Status, DateTime CreatedAt,
    string BusinessUnit, string? Division,
    string? Supplier, string BrandManufacturer, string Consignee,
    string Incoterm, string PaymentTerm, string ApprovalType,
    decimal? TotalOrderValueUsd,
    List<PoLineItemDetail> LineItems,
    List<PoOffshorePartnerDetail> OffshorePartners);

[ApiController]
[Authorize]
[Route("api/orders/{id:int}/details")]
public class PurchaseOrderDetailController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public PurchaseOrderDetailController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<PurchaseOrderDetailResponse>> Get(int id, [FromServices] BuAccessService buAccess)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.BusinessUnit)
            .Include(p => p.Division)
            .Include(p => p.Supplier)
            .Include(p => p.BrandManufacturer)
            .Include(p => p.Consignee)
            .Include(p => p.Incoterm)
            .Include(p => p.SupplierPaymentTerm)
            .Include(p => p.ApprovalType)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductCategory)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductType)
            .Include(p => p.LineItems).ThenInclude(li => li.ModelProduct)
            .Include(p => p.LineItems).ThenInclude(li => li.UnitOfMeasure)
            .Include(p => p.LineItems).ThenInclude(li => li.Currency)
            .Include(p => p.OffshorePartners).ThenInclude(o => o.BusinessPartner)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null) return NotFound();
        if (!buAccess.SeesAllBus(User) && !buAccess.CanSeeBusinessUnit(User, po.BusinessUnitId)) return Forbid();

        var isClearance = User.IsInRole(AppRoles.Clearance);
        var maxSequence = po.OffshorePartners.Count > 0 ? po.OffshorePartners.Max(o => o.SequenceOrder) : 0;

        var lineItems = po.LineItems.Select(li => new PoLineItemDetail(
            li.ProductCategory?.Name ?? "", li.ProductType?.Name ?? "", li.ModelProduct?.Name ?? "",
            li.Qty, li.UnitOfMeasure?.Code ?? "",
            isClearance ? null : li.UnitPrice,
            isClearance ? null : li.Currency?.Code,
            isClearance ? null : li.Qty * li.UnitPrice
        )).ToList();

        var offshorePartners = po.OffshorePartners
            .OrderBy(o => o.SequenceOrder)
            .Where(o => !isClearance || o.SequenceOrder == maxSequence) // Clearance only sees the last one
            .Select(o => new PoOffshorePartnerDetail(o.SequenceOrder, o.BusinessPartner?.Name ?? "", o.SequenceOrder == maxSequence))
            .ToList();

        decimal? totalOrderValueUsd = isClearance ? null : po.LineItems.Sum(li => li.TotalUsd);

        return new PurchaseOrderDetailResponse(
            po.Id, po.PoNumber, po.Status.ToString(), po.CreatedAt,
            po.BusinessUnit!.Name, po.Division?.Name,
            isClearance ? null : po.Supplier?.Name,
            po.BrandManufacturer?.Name ?? "", po.Consignee?.Name ?? "",
            po.Incoterm?.Name ?? "", po.SupplierPaymentTerm?.Name ?? "", po.ApprovalType?.Name ?? "",
            totalOrderValueUsd, lineItems, offshorePartners);
    }
}
