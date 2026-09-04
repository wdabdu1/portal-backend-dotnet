using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

// Every offshore-priced item ever entered, across all shipments — the C
// Pricing role's "History" page. One row per item (a multi-item BL repeats
// its BL number across rows), so C_Cat/C_Type/HS Code/Description/CP can
// be searched at the same granularity they were entered at.
//
// Moved from api/price-history (Finance-visible "CP History") to
// api/c-pricing/history and re-scoped to AppRoles.CPricingUsers — this
// page is no longer shown to Treasury/CorpFinance at all, per the C
// Pricing feature's access design.
public record PriceHistoryRow(
    string BusinessUnit, string BlAwbNo, DateOnly? ActualArrivalDate, string Category,
    string ModelProduct, string? CPricingCategory, string? CPricingType, string? HsCode,
    string? Description, decimal? CostPrice, string? Currency, DateTime? ApprovalDate);

[ApiController]
[Authorize(Roles = AppRoles.CPricingUsers)]
[Route("api/c-pricing/history")]
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
            .Include(i => i.CPricingCategory)
            .Include(i => i.CPricingType)
            .Include(i => i.Currency)
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
                i.CPricingCategory?.Name,
                i.CPricingType?.Name,
                shipLine.HsCode,
                i.Description,
                i.UnitPrice,
                i.Currency?.Code ?? lastOffshoreCurrencies.GetValueOrDefault(ship.Id),
                i.CPricingSavedAt);
        }).OrderByDescending(r => r.ApprovalDate).ToList();

        return Ok(rows);
    }
}
