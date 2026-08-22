using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

// Every offshore-priced item ever entered, across all shipments — lets
// Corp Finance (and whoever is setting a new price) search prior items
// by BU/Category and see what price was set last time, for consistency.
public record PriceHistoryRow(
    string BusinessUnit, string BlAwbNo, DateOnly? ActualArrivalDate, string Category,
    string ModelProduct, string? HsCode, string? Description, decimal? CostPrice, string? Currency);

[ApiController]
[Authorize(Roles = AppRoles.BankDuesViewers)]
[Route("api/price-history")]
public class PriceHistoryController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly BuAccessService _buAccess;
    public PriceHistoryController(ShippingPortalDbContext db, BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PriceHistoryRow>>> GetAll()
    {
        var query = _db.LastOffshoreItemDetails
            .Include(i => i.ShipmentLineItem!).ThenInclude(sl => sl.Shipment!).ThenInclude(s => s.PurchaseOrder!).ThenInclude(po => po.BusinessUnit)
            .Include(i => i.ShipmentLineItem!).ThenInclude(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ProductCategory)
            .Include(i => i.ShipmentLineItem!).ThenInclude(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .AsQueryable();

        if (!_buAccess.SeesAllBus(User))
        {
            var allowedBus = _buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(i => allowedBus.Contains(i.ShipmentLineItem!.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var items = await query.ToListAsync();
        var shipmentIds = items.Select(i => i.ShipmentLineItem!.ShipmentId).Distinct().ToList();

        var lastOffshoreCurrencies = await _db.LastOffshoreDetails
            .Where(d => shipmentIds.Contains(d.ShipmentId))
            .Include(d => d.Currency)
            .ToDictionaryAsync(d => d.ShipmentId, d => d.Currency?.Code);

        var clearancesByShipment = await _db.Clearances
            .Where(c => shipmentIds.Contains(c.ShipmentId))
            .ToDictionaryAsync(c => c.ShipmentId, c => c.Id);
        var clearanceIds = clearancesByShipment.Values.ToList();
        var deliveryOrdersByClearance = await _db.ClearanceDeliveryOrders
            .Where(d => clearanceIds.Contains(d.ClearanceId))
            .ToDictionaryAsync(d => d.ClearanceId, d => d.ActualArrivalDate);

        DateOnly? ArrivalDateFor(int shipmentId) =>
            clearancesByShipment.TryGetValue(shipmentId, out var clearanceId) && deliveryOrdersByClearance.TryGetValue(clearanceId, out var date)
                ? date : null;

        var rows = items.Select(i =>
        {
            var shipLine = i.ShipmentLineItem!;
            var ship = shipLine.Shipment!;
            var poLine = shipLine.PurchaseOrderLineItem!;

            return new PriceHistoryRow(
                ship.PurchaseOrder?.BusinessUnit?.Name ?? "",
                ship.BlAwbNo,
                ArrivalDateFor(ship.Id),
                poLine.ProductCategory?.Name ?? "",
                poLine.ModelProduct?.Name ?? "",
                shipLine.HsCode,
                i.Description,
                i.UnitPrice,
                lastOffshoreCurrencies.GetValueOrDefault(ship.Id));
        }).OrderByDescending(r => r.ActualArrivalDate).ToList();

        return Ok(rows);
    }
}
