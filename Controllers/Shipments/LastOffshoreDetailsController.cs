using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Shipments;

public record LastOffshoreItemResponse(
    int ShipmentLineItemId, string BusinessUnit, string Category, string ModelProduct,
    string? HsCode, string? Description, decimal? UnitPrice, decimal Qty, decimal? Total);

public record LastOffshoreDetailsResponse(
    string? PiNo, string? InspectionNo, string? Grn, string? InvoiceNo, string? Remarks,
    int? CurrencyId, string? CurrencyCode, List<LastOffshoreItemResponse> Items, decimal GrandTotal);

public record LastOffshoreItemInput(int ShipmentLineItemId, string? HsCode, string? Description, decimal? UnitPrice);
public record SaveLastOffshoreDetailsRequest(
    string? InspectionNo, string? Grn, string? InvoiceNo, string? Remarks, int? CurrencyId,
    List<LastOffshoreItemInput> Items);

[ApiController]
[Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
[Route("api/shipments/{shipmentId:int}/last-offshore")]
public class LastOffshoreDetailsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.BuAccessService _buAccess;

    public LastOffshoreDetailsController(ShippingPortalDbContext db, ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

    [HttpGet]
    public async Task<ActionResult<LastOffshoreDetailsResponse>> Get(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId);
        var detail = await _db.LastOffshoreDetails.Include(d => d.Currency).FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);

        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .ToListAsync();

        var itemDetails = await _db.LastOffshoreItemDetails
            .Where(x => lineItems.Select(li => li.Id).Contains(x.ShipmentLineItemId))
            .ToDictionaryAsync(x => x.ShipmentLineItemId);

        var businessUnit = await _db.PurchaseOrders
            .Where(p => p.Id == shipment.PurchaseOrderId)
            .Include(p => p.BusinessUnit)
            .Select(p => p.BusinessUnit!.Name)
            .FirstOrDefaultAsync() ?? "";

        var items = lineItems.Select(li =>
        {
            itemDetails.TryGetValue(li.Id, out var extra);
            decimal? total = extra?.UnitPrice.HasValue == true ? li.QtyInBl * extra.UnitPrice.Value : null;
            return new LastOffshoreItemResponse(
                li.Id, businessUnit, li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                li.HsCode, extra?.Description, extra?.UnitPrice, li.QtyInBl, total);
        }).ToList();

        return Ok(new LastOffshoreDetailsResponse(
            mot?.OffshoreApprovedPiNumber, detail?.InspectionNo, detail?.Grn, detail?.InvoiceNo, detail?.Remarks,
            detail?.CurrencyId, detail?.Currency?.Code, items, items.Sum(i => i.Total ?? 0)));
    }

    [HttpPut]
    public async Task<IActionResult> Save(int shipmentId, SaveLastOffshoreDetailsRequest req)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();
        if (!_buAccess.CanWriteBusinessUnit(User, await _db.PurchaseOrders.Where(p => p.Id == shipment.PurchaseOrderId).Select(p => p.BusinessUnitId).FirstOrDefaultAsync()))
            return Forbid();

        var detail = await _db.LastOffshoreDetails.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);
        if (detail is null)
        {
            detail = new LastOffshoreDetail { ShipmentId = shipmentId };
            _db.LastOffshoreDetails.Add(detail);
        }
        detail.InspectionNo = req.InspectionNo;
        detail.Grn = req.Grn;
        detail.InvoiceNo = req.InvoiceNo;
        detail.Remarks = req.Remarks;
        detail.CurrencyId = req.CurrencyId;

        var lineItemIds = await _db.ShipmentLineItems.Where(li => li.ShipmentId == shipmentId).Select(li => li.Id).ToListAsync();

        foreach (var input in req.Items.Where(i => lineItemIds.Contains(i.ShipmentLineItemId)))
        {
            var li = await _db.ShipmentLineItems.FindAsync(input.ShipmentLineItemId);
            if (li is not null) li.HsCode = input.HsCode;

            var extra = await _db.LastOffshoreItemDetails.FirstOrDefaultAsync(x => x.ShipmentLineItemId == input.ShipmentLineItemId);
            if (extra is null)
            {
                extra = new LastOffshoreItemDetail { ShipmentLineItemId = input.ShipmentLineItemId };
                _db.LastOffshoreItemDetails.Add(extra);
            }
            extra.Description = input.Description;
            extra.UnitPrice = input.UnitPrice;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
