using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using System.Security.Claims;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ShipmentLineItemRequest(int PurchaseOrderLineItemId, decimal QtyInBl);

public record CreateShipmentRequest(
    string BlAwbNo, int PurchaseOrderId, DateOnly? BlAwbDate, DateOnly? Etd, DateOnly? Eta,
    int ShippingLineId, int Fcl20Count, int Fcl40Count, bool Soc, int? BlFreeDays,
    List<ShipmentLineItemRequest> LineItems);

public record ShipmentSummary(int Id, string BlAwbNo, string PoNumber, string ShippingLine, string Status, DateOnly? Eta, int LineItemCount);

[ApiController]
[Authorize]
[Route("api/shipments")]
public class ShipmentsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShipmentsController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Roles = AppRoles.OrdersShipmentsViewers)]
    public async Task<ActionResult<IEnumerable<ShipmentSummary>>> GetAll([FromServices] BuAccessService buAccess)
    {
        var query = _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.ShippingLine)
            .Include(s => s.LineItems)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowed = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowed.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShipmentSummary(
                s.Id, s.BlAwbNo, s.PurchaseOrder!.PoNumber, s.ShippingLine!.Name,
                s.Status.ToString(), s.Eta, s.LineItems.Count))
            .ToListAsync();
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<ActionResult<ShipmentSummary>> Create(CreateShipmentRequest req, [FromServices] Services.BuAccessService buAccess)
    {
        var po = await _db.PurchaseOrders.Include(p => p.LineItems).FirstOrDefaultAsync(p => p.Id == req.PurchaseOrderId);
        if (po is null) return NotFound(new { message = "Purchase order not found." });
        if (!buAccess.CanWriteBusinessUnit(User, po.BusinessUnitId)) return Forbid();
        if (po.Status != OrderStatus.Confirmed) return BadRequest(new { message = "Shipments can only be created against confirmed purchase orders." });

        if (await _db.Shipments.AnyAsync(s => s.BlAwbNo == req.BlAwbNo))
            return Conflict(new { message = $"BL/AWB number '{req.BlAwbNo}' already exists." });

        if (req.LineItems.Count == 0)
            return BadRequest(new { message = "At least one line item is required." });

        var lineItemIds = req.LineItems.Select(li => li.PurchaseOrderLineItemId).ToList();
        var poLineItems = po.LineItems.Where(li => lineItemIds.Contains(li.Id)).ToDictionary(li => li.Id);

        var alreadyShipped = await _db.ShipmentLineItems
            .Where(sli => lineItemIds.Contains(sli.PurchaseOrderLineItemId) && sli.Shipment!.Status != ShipmentStatus.Cancelled)
            .GroupBy(sli => sli.PurchaseOrderLineItemId)
            .Select(g => new { PoLineItemId = g.Key, Shipped = g.Sum(x => x.QtyInBl) })
            .ToDictionaryAsync(x => x.PoLineItemId, x => x.Shipped);

        foreach (var li in req.LineItems)
        {
            if (!poLineItems.TryGetValue(li.PurchaseOrderLineItemId, out var poLine))
                return BadRequest(new { message = $"Line item {li.PurchaseOrderLineItemId} does not belong to this purchase order." });

            var shipped = alreadyShipped.GetValueOrDefault(li.PurchaseOrderLineItemId, 0m);
            var remaining = poLine.Qty - shipped;
            if (li.QtyInBl > remaining)
                return BadRequest(new { message = $"Quantity {li.QtyInBl} exceeds remaining {remaining} for this line item." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        var shipment = new Shipment
        {
            BlAwbNo = req.BlAwbNo,
            PurchaseOrderId = req.PurchaseOrderId,
            BlAwbDate = req.BlAwbDate,
            Etd = req.Etd,
            Eta = req.Eta,
            ShippingLineId = req.ShippingLineId,
            Fcl20Count = req.Fcl20Count,
            Fcl40Count = req.Fcl40Count,
            Soc = req.Soc,
            BlFreeDays = req.BlFreeDays,
            Status = ShipmentStatus.Draft,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var li in req.LineItems)
        {
            var poLine = poLineItems[li.PurchaseOrderLineItemId];
            shipment.LineItems.Add(new ShipmentLineItem
            {
                PurchaseOrderLineItemId = li.PurchaseOrderLineItemId,
                QtyInBl = li.QtyInBl,
                ItemSubtotal = li.QtyInBl * poLine.UnitPrice
            });
        }

        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var shippingLine = await _db.ShippingLines.FindAsync(req.ShippingLineId);
        return CreatedAtAction(nameof(GetAll), new ShipmentSummary(
            shipment.Id, shipment.BlAwbNo, po.PoNumber, shippingLine?.Name ?? "", shipment.Status.ToString(), shipment.Eta, shipment.LineItems.Count));
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
    public async Task<IActionResult> Confirm(int id, [FromServices] Services.BuAccessService buAccess)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).FirstOrDefaultAsync(s => s.Id == id);
        if (shipment is null) return NotFound();
        if (!buAccess.CanWriteBusinessUnit(User, shipment.PurchaseOrder!.BusinessUnitId)) return Forbid();
        if (shipment.Status != ShipmentStatus.Draft) return BadRequest(new { message = "Only draft shipments can be confirmed." });

        shipment.Status = ShipmentStatus.Confirmed;
        shipment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
