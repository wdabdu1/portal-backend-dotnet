using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.CPricing;

// The C Pricing role's daily working table: one row per ShipmentLineItem
// across every shipment, joined with whatever C Pricing data has been
// saved against it so far (LastOffshoreItemDetail — the same table the
// old "Last Offshore Details" item editor used, now written exclusively
// from this page instead). A multi-item BL naturally produces multiple
// rows sharing the same BlAwbNo, same as the source table always has.
public record CPricingItemRow(
    int ShipmentLineItemId, int BusinessUnitId, string BusinessUnit, string BlAwbNo, string Category, string ModelProduct,
    DateOnly? Eta, int? CPricingCategoryId, string? CPricingCategoryName, int? CPricingTypeId, string? CPricingTypeName,
    string? HsCode, string? Description, int? CurrencyId, string? CurrencyCode, decimal? Cp, decimal? PoUnitPriceUsd, bool IsConfirmed);

public record SaveCPricingItemRequest(int? CPricingCategoryId, int? CPricingTypeId, string? HsCode, string? Description, int? CurrencyId, decimal? Cp);

[ApiController]
[Authorize(Roles = AppRoles.CPricingUsers)]
[Route("api/c-pricing")]
public class CPricingController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly BuAccessService _buAccess;

    public CPricingController(ShippingPortalDbContext db, BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<CPricingItemRow>>> GetItems()
    {
        var query = _db.ShipmentLineItems
            .Include(li => li.Shipment!).ThenInclude(s => s.PurchaseOrder!).ThenInclude(po => po.BusinessUnit)
            .Include(li => li.PurchaseOrderLineItem!).ThenInclude(pl => pl.ProductCategory)
            .Include(li => li.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .AsQueryable();

        if (!_buAccess.SeesAllBus(User))
        {
            var allowed = _buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(li => allowed.Contains(li.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var lineItems = await query.ToListAsync();
        var itemIds = lineItems.Select(li => li.Id).ToList();

        var extras = await _db.LastOffshoreItemDetails
            .Include(x => x.CPricingCategory)
            .Include(x => x.CPricingType)
            .Include(x => x.Currency)
            .Where(x => itemIds.Contains(x.ShipmentLineItemId))
            .ToDictionaryAsync(x => x.ShipmentLineItemId);

        var shipmentIds = lineItems.Select(li => li.ShipmentId).Distinct().ToList();
        var shipmentLevelCurrencies = await _db.LastOffshoreDetails
            .Where(d => shipmentIds.Contains(d.ShipmentId))
            .Include(d => d.Currency)
            .ToDictionaryAsync(d => d.ShipmentId, d => d.Currency);

        var rows = lineItems.Select(li =>
        {
            extras.TryGetValue(li.Id, out var extra);
            var currency = extra?.Currency ?? shipmentLevelCurrencies.GetValueOrDefault(li.ShipmentId);

            var isConfirmed = extra is not null
                && extra.CPricingCategoryId.HasValue
                && extra.CPricingTypeId.HasValue
                && !string.IsNullOrWhiteSpace(li.HsCode)
                && !string.IsNullOrWhiteSpace(extra.Description)
                && currency is not null
                && extra.UnitPrice.HasValue;

            // Read-only reference figure — the unit price actually agreed on
            // the Purchase Order, converted to USD (PurchaseOrderLineItem's
            // own Total/TotalUsd are per the PO's full ordered Qty, so unit
            // price is constant regardless of how much of that PO ended up
            // on this particular BL/shipment).
            var poLine = li.PurchaseOrderLineItem;
            decimal? poUnitPriceUsd = poLine is not null && poLine.Qty != 0 ? poLine.TotalUsd / poLine.Qty : null;

            return new CPricingItemRow(
                li.Id,
                li.Shipment!.PurchaseOrder!.BusinessUnitId,
                li.Shipment.PurchaseOrder.BusinessUnit?.Name ?? "",
                li.Shipment.BlAwbNo,
                li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "",
                li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                li.Shipment.Eta,
                extra?.CPricingCategoryId, extra?.CPricingCategory?.Name,
                extra?.CPricingTypeId, extra?.CPricingType?.Name,
                li.HsCode, extra?.Description,
                currency?.Id, currency?.Code,
                extra?.UnitPrice,
                poUnitPriceUsd,
                isConfirmed);
        }).ToList();

        return Ok(rows);
    }

    [HttpPut("items/{shipmentLineItemId:int}")]
    public async Task<IActionResult> SaveItem(int shipmentLineItemId, SaveCPricingItemRequest req)
    {
        var li = await _db.ShipmentLineItems
            .Include(x => x.Shipment!).ThenInclude(s => s.PurchaseOrder)
            .FirstOrDefaultAsync(x => x.Id == shipmentLineItemId);
        if (li is null) return NotFound();

        if (!_buAccess.CanWriteBusinessUnit(User, li.Shipment!.PurchaseOrder!.BusinessUnitId))
            return Forbid();

        li.HsCode = req.HsCode;

        var extra = await _db.LastOffshoreItemDetails.FirstOrDefaultAsync(x => x.ShipmentLineItemId == shipmentLineItemId);
        if (extra is null)
        {
            extra = new LastOffshoreItemDetail { ShipmentLineItemId = shipmentLineItemId };
            _db.LastOffshoreItemDetails.Add(extra);
        }
        extra.Description = req.Description;
        extra.UnitPrice = req.Cp;
        extra.CPricingCategoryId = req.CPricingCategoryId;
        extra.CPricingTypeId = req.CPricingTypeId;
        extra.CurrencyId = req.CurrencyId;
        extra.CPricingSavedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
