using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Services;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Orders;

public record LineItemRemainingResponse(int Id, string ProductCategory, string ModelProduct, string ProductType, decimal Qty, decimal QtyShipped, decimal QtyRemaining, string UnitOfMeasure, decimal UnitPrice, string Currency);
public record LineItemRequest(
    int ProductCategoryId, int ModelProductId, int ProductTypeId,
    [Range(typeof(decimal), "0.0001", "79228162514264337593543950335", ErrorMessage = "Qty must be greater than zero.")] decimal Qty,
    int UnitOfMeasureId,
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Unit Price cannot be negative.")] decimal UnitPrice,
    int CurrencyId);
public record OffshorePartnerRequest(int BusinessPartnerId, int SequenceOrder);

public record CreatePurchaseOrderRequest(
    [Required(ErrorMessage = "PO Number is required."), MaxLength(50)] string PoNumber,
    int BusinessUnitId, int DivisionId, int SupplierId, int BrandManufacturerId, int ApprovalTypeId, int ConsigneeId,
    string? SupplierPiNo, DateOnly? SupplierPiDate, int SupplierPaymentTermId, int IncotermId, int OriginCountryId,
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "BU Shipping Budget cannot be negative.")] decimal? BuShippingBudget,
    int ShipmentModeId,
    string? OffshorePoNo, DateOnly? OffshorePoDate, DateOnly? ReceivedSignedPiDate, DateOnly? SentSignedPiDate, DateOnly? BuPoDate, DateOnly? OrderExecutionDate, DateOnly? LatestShippingDate,
    List<LineItemRequest> LineItems, List<OffshorePartnerRequest> OffshorePartners);

public record PurchaseOrderSummary(int Id, string PoNumber, string BusinessUnit, string Supplier, string Status, int LineItemCount, DateTime CreatedAt, decimal OrderValueUsd, decimal PercentShipped);
public record ConfirmedOrderOption(int Id, string PoNumber, string BusinessUnit, string Supplier, decimal OrderValueUsd, int BusinessUnitId, int SupplierId, int DivisionId);

public record LineItemResponse(int Id, string ProductCategory, string ModelProduct, string ProductType, decimal Qty, string UnitOfMeasure, decimal UnitPrice, string Currency, decimal Total, decimal TotalUsd);
public record OffshorePartnerResponse(int Id, string BusinessPartnerName, int SequenceOrder);

public record PurchaseOrderResponse(
    int Id, string PoNumber, string BusinessUnit, string Division, string Supplier, string BrandManufacturer,
    string ApprovalType, string Consignee, string Status, DateTime CreatedAt,
    List<LineItemResponse> LineItems, List<OffshorePartnerResponse> OffshorePartners);

[ApiController]
[Authorize]
[Route("api/purchase-orders")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly FxRateService _fx;

    public PurchaseOrdersController(ShippingPortalDbContext db, FxRateService fx)
    {
        _db = db;
        _fx = fx;
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.OrdersShipmentsViewers)]
    public async Task<ActionResult<IEnumerable<PurchaseOrderSummary>>> GetAll([FromServices] BuAccessService buAccess)
    {
        var query = _db.PurchaseOrders
            .Include(p => p.BusinessUnit)
            .Include(p => p.Supplier)
            .Include(p => p.LineItems)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowed = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(p => allowed.Contains(p.BusinessUnitId));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PurchaseOrderSummary(
                p.Id, p.PoNumber, p.BusinessUnit!.Name, p.Supplier!.Name,
                p.Status.ToString(), p.LineItems.Count, p.CreatedAt,
                p.LineItems.Sum(li => li.TotalUsd),
                p.LineItems.Sum(li => li.Qty) == 0 ? 0 :
                    (_db.ShipmentLineItems.Where(sli => p.LineItems.Select(li => li.Id).Contains(sli.PurchaseOrderLineItemId)).Sum(sli => sli.QtyInBl)
                        / p.LineItems.Sum(li => li.Qty)) * 100))
            .ToListAsync();
    }
    // Carries the raw Supplier/BusinessUnit/Division ids (not just their
    // names) so the New Shipment page can work out, client-side, which
    // other confirmed orders could be combined with a given one into one
    // shipment (same Supplier + BU + Division) — the actual enforcement
    // happens server-side in ShipmentsController.Create(), this is only
    // for not offering a combination that's guaranteed to be rejected.
    [HttpGet("confirmed")]
    public async Task<ActionResult<IEnumerable<ConfirmedOrderOption>>> GetConfirmed()
    {
        return await _db.PurchaseOrders
            .Where(p => p.Status == OrderStatus.Confirmed)
            .Include(p => p.BusinessUnit)
            .Include(p => p.Supplier)
            .Include(p => p.LineItems)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ConfirmedOrderOption(
                p.Id, p.PoNumber, p.BusinessUnit!.Name, p.Supplier!.Name, p.LineItems.Sum(li => li.TotalUsd),
                p.BusinessUnitId, p.SupplierId, p.DivisionId))
            .ToListAsync();
    }

    [HttpGet("{id:int}/line-items-remaining")]
    public async Task<ActionResult<IEnumerable<LineItemRemainingResponse>>> GetLineItemsRemaining(int id)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.LineItems).ThenInclude(li => li.ProductCategory)
            .Include(p => p.LineItems).ThenInclude(li => li.ModelProduct)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductType)
            .Include(p => p.LineItems).ThenInclude(li => li.UnitOfMeasure)
            .Include(p => p.LineItems).ThenInclude(li => li.Currency)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null) return NotFound();

        var lineItemIds = po.LineItems.Select(li => li.Id).ToList();

        var shippedByLineItem = await _db.ShipmentLineItems
            .Where(sli => lineItemIds.Contains(sli.PurchaseOrderLineItemId) && sli.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(sli => sli.PurchaseOrderLineItemId)
            .Select(g => new { PoLineItemId = g.Key, Shipped = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.PoLineItemId, x => x.Shipped);

        return po.LineItems.Select(li =>
        {
            var shipped = shippedByLineItem.GetValueOrDefault(li.Id, 0m);
            return new LineItemRemainingResponse(
                li.Id, li.ProductCategory!.Name, li.ModelProduct!.Name, li.ProductType!.Name,
                li.Qty, shipped, li.Qty - shipped, li.UnitOfMeasure!.Code, li.UnitPrice, li.Currency!.Code);
        }).ToList();
    }
    [HttpGet("{id:int}")]
    [Authorize(Roles = AppRoles.OrdersShipmentsViewers)]
    public async Task<ActionResult<PurchaseOrderResponse>> GetById(int id)
    {
        var po = await LoadFullOrderAsync(id);
        return po is null ? NotFound() : ToResponse(po);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<ActionResult<PurchaseOrderResponse>> Create(CreatePurchaseOrderRequest req)
    {
        if (!await HasWriteAccessAsync(req.BusinessUnitId))
            return Forbid();

        if (await _db.PurchaseOrders.AnyAsync(p => p.PoNumber == req.PoNumber))
            return Conflict(new { message = $"PO number '{req.PoNumber}' already exists." });

        if (req.LineItems.Count == 0)
            return BadRequest(new { message = "At least one line item is required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        var po = new PurchaseOrder
        {
            PoNumber = req.PoNumber,
            BusinessUnitId = req.BusinessUnitId,
            DivisionId = req.DivisionId,
            SupplierId = req.SupplierId,
            BrandManufacturerId = req.BrandManufacturerId,
            ApprovalTypeId = req.ApprovalTypeId,
            ConsigneeId = req.ConsigneeId,
            SupplierPiNo = req.SupplierPiNo,
            SupplierPiDate = req.SupplierPiDate,
            SupplierPaymentTermId = req.SupplierPaymentTermId,
            IncotermId = req.IncotermId,
            OriginCountryId = req.OriginCountryId,
            BuShippingBudget = req.BuShippingBudget,
            ShipmentModeId = req.ShipmentModeId,
            OffshorePoNo = req.OffshorePoNo,
            OffshorePoDate = req.OffshorePoDate,
            ReceivedSignedPiDate = req.ReceivedSignedPiDate,
            SentSignedPiDate = req.SentSignedPiDate,
            BuPoDate = req.BuPoDate,
            OrderExecutionDate = req.OrderExecutionDate,
            LatestShippingDate = req.LatestShippingDate,
            Status = OrderStatus.Draft,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var li in req.LineItems)
        {
            var total = li.Qty * li.UnitPrice;
            var rate = await _fx.GetRateToUsdAsync(li.CurrencyId);
            po.LineItems.Add(new PurchaseOrderLineItem
            {
                ProductCategoryId = li.ProductCategoryId,
                ModelProductId = li.ModelProductId,
                ProductTypeId = li.ProductTypeId,
                Qty = li.Qty,
                UnitOfMeasureId = li.UnitOfMeasureId,
                UnitPrice = li.UnitPrice,
                CurrencyId = li.CurrencyId,
                Total = total,
                TotalUsd = total / rate
            });
        }

        foreach (var op in req.OffshorePartners)
        {
            po.OffshorePartners.Add(new PurchaseOrderOffshorePartner
            {
                BusinessPartnerId = op.BusinessPartnerId,
                SequenceOrder = op.SequenceOrder
            });
        }

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var full = await LoadFullOrderAsync(po.Id);
        return CreatedAtAction(nameof(GetById), new { id = po.Id }, ToResponse(full!));
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<IActionResult> Confirm(int id)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po is null) return NotFound();
        if (!await HasWriteAccessAsync(po.BusinessUnitId)) return Forbid();
        if (po.Status != OrderStatus.Draft) return BadRequest(new { message = "Only draft orders can be confirmed." });

        po.Status = OrderStatus.Confirmed;
        po.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public record AdvancePaymentRequest(decimal? AdvancePaymentPercent, DateOnly? AdvancePaymentPlannedDate, DateOnly? AdvancePaymentExecutedDate);

    // The one deliberately editable piece of an otherwise-immutable PO —
    // the advance is typically agreed and paid weeks before the first
    // shipment exists, so Finance needs to record it, and later record
    // its real execution date, independent of the PO's own lifecycle.
    [HttpPut("{id:int}/advance-payment")]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<IActionResult> SaveAdvancePayment(int id, AdvancePaymentRequest req, [FromServices] PoAdvancePaymentService poAdvanceService)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po is null) return NotFound();
        if (!await HasWriteAccessAsync(po.BusinessUnitId)) return Forbid();

        po.AdvancePaymentPercent = req.AdvancePaymentPercent;
        po.AdvancePaymentPlannedDate = req.AdvancePaymentPlannedDate;
        po.AdvancePaymentExecutedDate = req.AdvancePaymentExecutedDate;
        po.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Propagate to every shipment that already has an Advance line
        // from this PO — this is what overwrites a still-"planned" line
        // with the real date/amount once execution is recorded.
        await poAdvanceService.SyncAllForPoAsync(id);

        return NoContent();
    }

    private async Task<PurchaseOrder?> LoadFullOrderAsync(int id)
    {
        return await _db.PurchaseOrders
            .Include(p => p.BusinessUnit)
            .Include(p => p.Division)
            .Include(p => p.Supplier)
            .Include(p => p.BrandManufacturer)
            .Include(p => p.ApprovalType)
            .Include(p => p.Consignee)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductCategory)
            .Include(p => p.LineItems).ThenInclude(li => li.ModelProduct)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductType)
            .Include(p => p.LineItems).ThenInclude(li => li.UnitOfMeasure)
            .Include(p => p.LineItems).ThenInclude(li => li.Currency)
            .Include(p => p.OffshorePartners).ThenInclude(op => op.BusinessPartner)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    private static PurchaseOrderResponse ToResponse(PurchaseOrder po) => new(
        po.Id, po.PoNumber, po.BusinessUnit!.Name, po.Division!.Name, po.Supplier!.Name,
        po.BrandManufacturer!.Name, po.ApprovalType!.Name, po.Consignee!.Name, po.Status.ToString(), po.CreatedAt,
        po.LineItems.Select(li => new LineItemResponse(
            li.Id, li.ProductCategory!.Name, li.ModelProduct!.Name, li.ProductType!.Name,
            li.Qty, li.UnitOfMeasure!.Code, li.UnitPrice, li.Currency!.Code, li.Total, li.TotalUsd)).ToList(),
        po.OffshorePartners.OrderBy(op => op.SequenceOrder).Select(op => new OffshorePartnerResponse(
            op.Id, op.BusinessPartner!.Name, op.SequenceOrder)).ToList());

    // Manager/SuperUser can write across all BUs; Standard users need explicit
    // ReadWrite access to the specific BU, per the access-control design.
    private Task<bool> HasWriteAccessAsync(int businessUnitId)
    {
        if (User.IsInRole(AppRoles.Manager) || User.IsInRole(AppRoles.SuperUser))
            return Task.FromResult(true);

        var hasAccess = User.Claims.Any(c =>
            c.Type == "bu" &&
            c.Value.StartsWith($"{businessUnitId}:") &&
            c.Value.EndsWith(":ReadWrite"));

        return Task.FromResult(hasAccess);
    }
}
