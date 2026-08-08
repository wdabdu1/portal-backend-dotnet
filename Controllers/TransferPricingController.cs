using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record TpStageResponse(
    int PurchaseOrderOffshorePartnerId, string CompanyName, int SequenceOrder, bool IsLast,
    decimal? MarkupPercent, int? CurrencyId, string? CurrencyCode, decimal? Total, decimal? TotalUsd);

public record TpLineItemResponse(
    int ShipmentLineItemId, string BusinessUnit, string Category, string ModelProduct,
    string BlAwbNo, decimal SupplierTotal, string SupplierCurrencyCode, decimal SupplierCnfUsd,
    List<TpStageResponse> Stages);

public record TpStageInput(int PurchaseOrderOffshorePartnerId, int CurrencyId, decimal? MarkupPercent);
public record SaveTpLineItemRequest(List<TpStageInput> Stages);

[ApiController]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.CorpFinance)]
[Route("api/transfer-pricing")]
public class TransferPricingController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.SectionLockService _sectionLock;
    private readonly Dictionary<int, decimal> _fxCache = new();

    public TransferPricingController(ShippingPortalDbContext db, ShippingPortal.Api.Services.SectionLockService sectionLock)
    {
        _db = db;
        _sectionLock = sectionLock;
    }

    // RateToUsd = units of this currency per 1 USD (matches the existing
    // convention used elsewhere in the app, e.g. Bank Dues' AED conversion).
    private async Task<decimal> GetRateToUsdAsync(int currencyId)
    {
        if (_fxCache.TryGetValue(currencyId, out var cached)) return cached;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var value = rate?.RateToUsd ?? 1m;
        _fxCache[currencyId] = value;
        return value;
    }

    private async Task<decimal> ToUsdAsync(decimal value, int currencyId)
    {
        var rate = await GetRateToUsdAsync(currencyId);
        return rate == 0 ? value : value / rate;
    }

    private async Task<decimal> FromUsdAsync(decimal valueUsd, int currencyId)
    {
        var rate = await GetRateToUsdAsync(currencyId);
        return valueUsd * rate;
    }

    [HttpGet("{shipmentId:int}")]
    public async Task<ActionResult<IEnumerable<TpLineItemResponse>>> GetShipment(int shipmentId)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var forwarder = await _db.ShipmentForwarders.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId);
        var freightUsd = forwarder?.ActualShippingCostUsd ?? 0m;

        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .ToListAsync();

        var offshorePartners = await _db.PurchaseOrderOffshorePartners
            .Where(op => op.PurchaseOrderId == shipment.PurchaseOrderId)
            .Include(op => op.BusinessPartner)
            .OrderBy(op => op.SequenceOrder)
            .ToListAsync();

        var maxSeq = offshorePartners.Count > 0 ? offshorePartners.Max(op => op.SequenceOrder) : 0;

        var lastOffshoreItems = await _db.LastOffshoreItemDetails
            .Where(x => lineItems.Select(li => li.Id).Contains(x.ShipmentLineItemId))
            .ToDictionaryAsync(x => x.ShipmentLineItemId);
        var lastOffshoreDetail = await _db.LastOffshoreDetails.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);

        var existingEntries = await _db.TransferPricingEntries
            .Where(e => lineItems.Select(li => li.Id).Contains(e.ShipmentLineItemId))
            .ToListAsync();

        // Freight is allocated proportionally to each item's share of the
        // shipment's total Supplier value (TotalUsd), since freight itself
        // is only known at the whole-shipment level.
        var totalSupplierUsd = lineItems.Sum(li => li.PurchaseOrderLineItem?.TotalUsd ?? 0m);

        var result = new List<TpLineItemResponse>();
        foreach (var li in lineItems)
        {
            var pli = li.PurchaseOrderLineItem!;
            var supplierTotalUsd = pli.TotalUsd;
            var freightShareUsd = totalSupplierUsd == 0 ? 0 : freightUsd * (supplierTotalUsd / totalSupplierUsd);
            var supplierCnfUsd = supplierTotalUsd + freightShareUsd;

            var stages = new List<TpStageResponse>();
            var runningUsd = supplierCnfUsd;

            foreach (var partner in offshorePartners)
            {
                var isLast = partner.SequenceOrder == maxSeq;
                var entry = existingEntries.FirstOrDefault(e => e.ShipmentLineItemId == li.Id && e.PurchaseOrderOffshorePartnerId == partner.Id);

                if (!isLast)
                {
                    if (entry is not null)
                    {
                        // Recompute fresh from the current running USD base,
                        // so an upstream markup change correctly cascades
                        // through every downstream stage.
                        var stageValueInCurrency = await FromUsdAsync(runningUsd, entry.CurrencyId);
                        var markup = entry.MarkupPercent ?? 0;
                        var total = stageValueInCurrency * (1 + markup / 100);
                        var totalUsd = await ToUsdAsync(total, entry.CurrencyId);

                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, false,
                            entry.MarkupPercent, entry.CurrencyId, (await _db.Currencies.FindAsync(entry.CurrencyId))?.Code, total, totalUsd));
                        runningUsd = totalUsd;
                    }
                    else
                    {
                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, false,
                            null, null, null, null, null));
                        // No entry yet for this stage — chain can't continue past this point.
                        break;
                    }
                }
                else
                {
                    // Last offshore: Total is pulled directly from Last Offshore
                    // Details' real invoice data, never calculated from a markup.
                    lastOffshoreItems.TryGetValue(li.Id, out var extra);
                    decimal? lastTotal = extra?.UnitPrice.HasValue == true ? li.QtyInBl * extra.UnitPrice.Value : null;
                    var lastCurrencyId = lastOffshoreDetail?.CurrencyId;

                    if (lastTotal.HasValue && lastCurrencyId.HasValue)
                    {
                        var lastTotalUsd = await ToUsdAsync(lastTotal.Value, lastCurrencyId.Value);
                        var markupPercent = runningUsd == 0 ? 0 : (lastTotalUsd - runningUsd) / runningUsd * 100;
                        var currencyCode = (await _db.Currencies.FindAsync(lastCurrencyId.Value))?.Code;

                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, true,
                            markupPercent, lastCurrencyId, currencyCode, lastTotal, lastTotalUsd));
                    }
                    else
                    {
                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, true,
                            null, lastCurrencyId, null, lastTotal, null));
                    }
                }
            }

            result.Add(new TpLineItemResponse(
                li.Id, shipment.PurchaseOrder!.BusinessUnit!.Name, pli.ProductCategory?.Name ?? "", pli.ModelProduct?.Name ?? "",
                shipment.BlAwbNo, pli.Total, pli.Currency?.Code ?? "", supplierCnfUsd, stages));
        }

        return Ok(result);
    }

    [HttpPut("line-item/{shipmentLineItemId:int}")]
    public async Task<IActionResult> SaveLineItem(int shipmentLineItemId, SaveTpLineItemRequest req)
    {
        var li = await _db.ShipmentLineItems.Include(x => x.PurchaseOrderLineItem).FirstOrDefaultAsync(x => x.Id == shipmentLineItemId);
        if (li is null) return NotFound();

        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", li.ShipmentId, "transferPricing");
        if (lockDenied is not null) return lockDenied;

        foreach (var input in req.Stages)
        {
            var entry = await _db.TransferPricingEntries.FirstOrDefaultAsync(e =>
                e.ShipmentLineItemId == shipmentLineItemId && e.PurchaseOrderOffshorePartnerId == input.PurchaseOrderOffshorePartnerId);

            if (entry is null)
            {
                entry = new TransferPricingEntry { ShipmentLineItemId = shipmentLineItemId, PurchaseOrderOffshorePartnerId = input.PurchaseOrderOffshorePartnerId };
                _db.TransferPricingEntries.Add(entry);
            }
            entry.CurrencyId = input.CurrencyId;
            entry.MarkupPercent = input.MarkupPercent;
            entry.UpdatedAt = DateTime.UtcNow;
            // Total/TotalUsd are recomputed live on every GET, not stored
            // redundantly here — avoids drift if an upstream stage changes later.
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
