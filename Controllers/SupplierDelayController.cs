using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers;

public record SupplierDelayLine(
    int PurchaseOrderId, string PoNumber, string BusinessUnit, string Supplier,
    string Category, string ModelProduct, decimal OrderedQty, decimal DispatchedQty, decimal PendingQty,
    DateOnly? LatestShippingDate, int? DaysRemaining, string UrgencyLevel);

// Flags PO line items that still have undispatched quantity as their
// Latest Shipping Date approaches — an early nudge to chase the
// supplier for status before it becomes a real problem. Only line
// items with genuine pending quantity ever appear here; a fully
// dispatched line simply isn't a risk anymore, regardless of date.
[ApiController]
[Route("api/dashboards/supplier-delay")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.Bu)]
public class SupplierDelayController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SupplierDelayController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDelayLine>>> Get(
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess,
        [FromQuery] int? businessUnitId, [FromQuery] int? supplierId)
    {
        var query = _db.PurchaseOrders
            .Where(p => p.Status != OrderStatus.Cancelled)
            .Include(p => p.BusinessUnit)
            .Include(p => p.Supplier)
            .Include(p => p.LineItems).ThenInclude(li => li.ProductCategory)
            .Include(p => p.LineItems).ThenInclude(li => li.ModelProduct)
            .AsQueryable();

        if (businessUnitId.HasValue) query = query.Where(p => p.BusinessUnitId == businessUnitId);
        if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId);

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(p => allowedBus.Contains(p.BusinessUnitId));
        }

        var pos = await query.ToListAsync();
        var poIds = pos.Select(p => p.Id).ToList();
        var lineItemIds = pos.SelectMany(p => p.LineItems).Select(li => li.Id).ToList();

        var dispatchedByLineItem = await _db.ShipmentLineItems
            .Where(sl => lineItemIds.Contains(sl.PurchaseOrderLineItemId))
            .GroupBy(sl => sl.PurchaseOrderLineItemId)
            .Select(g => new { LineItemId = g.Key, Total = g.Sum(sl => sl.QtyInBl) })
            .ToDictionaryAsync(x => x.LineItemId, x => x.Total);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<SupplierDelayLine>();

        foreach (var po in pos)
        {
            if (!po.LatestShippingDate.HasValue) continue;

            foreach (var li in po.LineItems)
            {
                var dispatched = dispatchedByLineItem.GetValueOrDefault(li.Id, 0m);
                var pending = li.Qty - dispatched;
                if (pending <= 0) continue;

                var daysRemaining = po.LatestShippingDate.Value.DayNumber - today.DayNumber;

                // Only surface once it's within a month of the deadline —
                // anything further out isn't worth a nudge yet.
                if (daysRemaining > 30) continue;

                var urgency = daysRemaining <= 0 ? "Red" : daysRemaining <= 14 ? "Amber" : "Light";

                result.Add(new SupplierDelayLine(
                    po.Id, po.PoNumber, po.BusinessUnit?.Name ?? "", po.Supplier?.Name ?? "",
                    li.ProductCategory?.Name ?? "", li.ModelProduct?.Name ?? "", li.Qty, dispatched, pending,
                    po.LatestShippingDate, daysRemaining, urgency));
            }
        }

        return Ok(result.OrderBy(r => r.DaysRemaining).ToList());
    }
}
