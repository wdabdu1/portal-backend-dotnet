using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record TpStageResponse(
    int PurchaseOrderOffshorePartnerId, string CompanyName, int SequenceOrder, bool IsLast,
    decimal? MarkupPercent, int? CurrencyId, string? CurrencyCode, decimal? Total, decimal? TotalUsd,
    // True when MarkupPercent/CurrencyId came from Settings/TP's default
    // rather than a value someone actually entered and saved for this
    // shipment — purely informational, still fully editable either way.
    bool IsDefault);

public record TpLineItemResponse(
    int ShipmentLineItemId, string BusinessUnit, string Category, string ModelProduct,
    string BlAwbNo, decimal SupplierTotal, string SupplierCurrencyCode, decimal SupplierTotalUsd, decimal SupplierCnfUsd,
    List<TpStageResponse> Stages);

public record TpStageInput(int PurchaseOrderOffshorePartnerId, int CurrencyId, decimal? MarkupPercent);
public record SaveTpLineItemRequest(List<TpStageInput> Stages);

public record TpOrderSummary(
    int ShipmentId, string BlAwbNo, string PoNumber, string BusinessUnit, string SupplierName,
    decimal SupplierValueUsd, DateTime CreatedAt, List<string> RouteCompanyNames, bool IsConfirmed);

public record BuStageAccumulated(int SequenceOrder, bool IsLast, decimal TotalUsd, decimal MarkupPercent);
public record BuAccumulatedRow(string BusinessUnit, decimal TotalSupplierUsd, List<BuStageAccumulated> Stages);

// One row per offshore company, across every BU and every chain position
// it's ever occupied — answers "which entity is profitable" directly,
// independent of where in a given chain they happened to sit.
public record OffshoreCompanyAccumulated(string CompanyName, decimal AccumulatedRevenueUsd, decimal AccumulatedMarkupUsd, decimal MarkupPercent);

[ApiController]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.CorpFinance)]
[Route("api/transfer-pricing")]
public class TransferPricingController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.SectionLockService _sectionLock;
    private readonly Dictionary<int, decimal> _fxCache = new();
    private Dictionary<int, string>? _currencyCodeCache;
    private int? _usdCurrencyId;

    private async Task<Dictionary<int, string>> GetCurrencyCodesAsync()
    {
        if (_currencyCodeCache is not null) return _currencyCodeCache;
        _currencyCodeCache = await _db.Currencies.ToDictionaryAsync(c => c.Id, c => c.Code);
        return _currencyCodeCache;
    }

    public TransferPricingController(ShippingPortalDbContext db, ShippingPortal.Api.Services.SectionLockService sectionLock)
    {
        _db = db;
        _sectionLock = sectionLock;
    }

    private async Task<int> GetUsdCurrencyIdAsync()
    {
        if (_usdCurrencyId.HasValue) return _usdCurrencyId.Value;
        var usd = await _db.Currencies.FirstOrDefaultAsync(c => c.Code == "USD");
        _usdCurrencyId = usd?.Id ?? 0;
        return _usdCurrencyId.Value;
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

    // Core calculation, shared by the single-shipment view and the
    // accumulated report — computes every line item's full cascade for
    // one shipment. Last Offshore's total/margin is always derived live
    // here, never persisted, since it depends on Last Offshore Details.
    private async Task<List<TpLineItemResponse>> ComputeShipmentAsync(int shipmentId)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return new List<TpLineItemResponse>();

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

        var offshoreBusinessPartnerIds = offshorePartners.Select(op => op.BusinessPartnerId).Distinct().ToList();
        var markupDefaults = await _db.OffshoreMarkupDefaults
            .Where(d => offshoreBusinessPartnerIds.Contains(d.BusinessPartnerId))
            .ToDictionaryAsync(d => d.BusinessPartnerId);

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
                    markupDefaults.TryGetValue(partner.BusinessPartnerId, out var markupDefault);
                    var isDefault = entry is null;

                    var stageCurrencyId = entry?.CurrencyId ?? markupDefault?.DefaultCurrencyId ?? await GetUsdCurrencyIdAsync();
                    var markup = entry?.MarkupPercent ?? markupDefault?.DefaultMarkupPercent ?? 0;

                    var stageValueInCurrency = await FromUsdAsync(runningUsd, stageCurrencyId);
                    var total = stageValueInCurrency * (1 + markup / 100);
                    var totalUsd = await ToUsdAsync(total, stageCurrencyId);
                    var currencyCodes = await GetCurrencyCodesAsync();
                    currencyCodes.TryGetValue(stageCurrencyId, out var currencyCode);

                    stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, false,
                        markup, stageCurrencyId, currencyCode, total, totalUsd, isDefault));
                    runningUsd = totalUsd;
                }
                else
                {
                    lastOffshoreItems.TryGetValue(li.Id, out var extra);
                    decimal? lastTotal = extra?.UnitPrice.HasValue == true ? li.QtyInBl * extra.UnitPrice.Value : null;
                    var lastCurrencyId = lastOffshoreDetail?.CurrencyId;

                    if (lastTotal.HasValue && lastCurrencyId.HasValue)
                    {
                        var lastTotalUsd = await ToUsdAsync(lastTotal.Value, lastCurrencyId.Value);
                        var markupPercent = runningUsd == 0 ? 0 : (lastTotalUsd - runningUsd) / runningUsd * 100;
                        var currencyCodes = await GetCurrencyCodesAsync();
                        currencyCodes.TryGetValue(lastCurrencyId.Value, out var currencyCode);

                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, true,
                            markupPercent, lastCurrencyId, currencyCode, lastTotal, lastTotalUsd, false));
                    }
                    else
                    {
                        stages.Add(new TpStageResponse(partner.Id, partner.BusinessPartner!.Name, partner.SequenceOrder, true,
                            null, lastCurrencyId, null, lastTotal, null, false));
                    }
                }
            }

            result.Add(new TpLineItemResponse(
                li.Id, shipment.PurchaseOrder!.BusinessUnit!.Name, pli.ProductCategory?.Name ?? "", pli.ModelProduct?.Name ?? "",
                shipment.BlAwbNo, pli.Total, pli.Currency?.Code ?? "", supplierTotalUsd, supplierCnfUsd, stages));
        }

        return result;
    }

    [HttpGet("{shipmentId:int}")]
    public async Task<ActionResult<IEnumerable<TpLineItemResponse>>> GetShipment(int shipmentId)
        => Ok(await ComputeShipmentAsync(shipmentId));

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
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // "By Orders" — every shipment with an offshore chain (TP-eligible),
    // with its Pending/Confirmed status based on the same lock used to
    // protect saved data.
    [HttpGet("orders")]
    public async Task<ActionResult<IEnumerable<TpOrderSummary>>> GetOrders()
    {
        var eligiblePoIds = await _db.PurchaseOrderOffshorePartners.Select(op => op.PurchaseOrderId).Distinct().ToListAsync();

        var shipments = await _db.Shipments
            .Where(s => eligiblePoIds.Contains(s.PurchaseOrderId))
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .ToListAsync();

        var lockedShipmentIds = await _db.SectionLocks
            .Where(l => l.EntityType == "Shipment" && l.SectionKey == "transferPricing")
            .Select(l => l.EntityId)
            .ToListAsync();

        var poIds = shipments.Select(s => s.PurchaseOrderId).Distinct().ToList();
        var offshoreChains = await _db.PurchaseOrderOffshorePartners
            .Where(op => poIds.Contains(op.PurchaseOrderId))
            .Include(op => op.BusinessPartner)
            .OrderBy(op => op.SequenceOrder)
            .ToListAsync();

        var supplierValueByShipment = await _db.ShipmentLineItems
            .Where(li => shipments.Select(s => s.Id).Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem)
            .GroupBy(li => li.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, TotalUsd = g.Sum(li => li.PurchaseOrderLineItem!.TotalUsd) })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.TotalUsd);

        return Ok(shipments.Select(s =>
        {
            var route = offshoreChains.Where(op => op.PurchaseOrderId == s.PurchaseOrderId).Select(op => op.BusinessPartner!.Name).ToList();
            return new TpOrderSummary(
                s.Id, s.BlAwbNo, s.PurchaseOrder!.PoNumber, s.PurchaseOrder.BusinessUnit!.Name, s.PurchaseOrder.Supplier?.Name ?? "",
                supplierValueByShipment.GetValueOrDefault(s.Id), s.CreatedAt, route, lockedShipmentIds.Contains(s.Id));
        }).OrderByDescending(o => o.CreatedAt).ToList());
    }

    // "Accumulated Orders History" — grouped by BU, across every CONFIRMED
    // (locked) shipment only, since in-progress drafts shouldn't count
    // toward accepted profitability history. Markup % per stage is a
    // blended figure computed from aggregate totals (ΣStage − ΣPrevious) /
    // ΣPrevious, not an average of individual percentages, since bases differ.
    [HttpGet("accumulated")]
    public async Task<ActionResult<IEnumerable<BuAccumulatedRow>>> GetAccumulated()
    {
        var confirmedShipmentIds = await _db.SectionLocks
            .Where(l => l.EntityType == "Shipment" && l.SectionKey == "transferPricing")
            .Select(l => l.EntityId)
            .ToListAsync();

        // (BU, SequenceOrder) -> running sums
        var supplierByBu = new Dictionary<string, decimal>();
        var stageByBuAndSeq = new Dictionary<(string Bu, int Seq), (decimal TotalUsd, bool IsLast)>();
        var previousByBuAndSeq = new Dictionary<(string Bu, int Seq), decimal>();

        foreach (var shipmentId in confirmedShipmentIds)
        {
            var items = await ComputeShipmentAsync(shipmentId);
            foreach (var item in items)
            {
                supplierByBu[item.BusinessUnit] = supplierByBu.GetValueOrDefault(item.BusinessUnit) + item.SupplierCnfUsd;

                var running = item.SupplierCnfUsd;
                foreach (var stage in item.Stages)
                {
                    var key = (item.BusinessUnit, stage.SequenceOrder);
                    var totalUsd = stage.TotalUsd ?? 0;

                    var existing = stageByBuAndSeq.GetValueOrDefault(key, (TotalUsd: 0m, IsLast: stage.IsLast));
                    stageByBuAndSeq[key] = (TotalUsd: existing.TotalUsd + totalUsd, IsLast: stage.IsLast);
                    previousByBuAndSeq[key] = previousByBuAndSeq.GetValueOrDefault(key) + running;

                    running = totalUsd;
                }
            }
        }

        var result = supplierByBu.Keys.Select(bu =>
        {
            var stages = stageByBuAndSeq
                .Where(kv => kv.Key.Bu == bu)
                .OrderBy(kv => kv.Key.Seq)
                .Select(kv =>
                {
                    var previousTotal = previousByBuAndSeq.GetValueOrDefault(kv.Key);
                    var markup = previousTotal == 0 ? 0 : (kv.Value.TotalUsd - previousTotal) / previousTotal * 100;
                    return new BuStageAccumulated(kv.Key.Seq, kv.Value.IsLast, kv.Value.TotalUsd, markup);
                })
                .ToList();

            return new BuAccumulatedRow(bu, supplierByBu[bu], stages);
        }).OrderBy(r => r.BusinessUnit).ToList();

        return Ok(result);
    }

    [HttpGet("accumulated-by-offshore")]
    public async Task<ActionResult<IEnumerable<OffshoreCompanyAccumulated>>> GetAccumulatedByOffshore()
    {
        var confirmedShipmentIds = await _db.SectionLocks
            .Where(l => l.EntityType == "Shipment" && l.SectionKey == "transferPricing")
            .Select(l => l.EntityId)
            .ToListAsync();

        var revenueByCompany = new Dictionary<string, decimal>();
        var previousTotalByCompany = new Dictionary<string, decimal>();

        foreach (var shipmentId in confirmedShipmentIds)
        {
            var items = await ComputeShipmentAsync(shipmentId);
            foreach (var item in items)
            {
                var running = item.SupplierCnfUsd;
                foreach (var stage in item.Stages)
                {
                    var totalUsd = stage.TotalUsd ?? 0;
                    revenueByCompany[stage.CompanyName] = revenueByCompany.GetValueOrDefault(stage.CompanyName) + totalUsd;
                    previousTotalByCompany[stage.CompanyName] = previousTotalByCompany.GetValueOrDefault(stage.CompanyName) + running;
                    running = totalUsd;
                }
            }
        }

        var result = revenueByCompany.Keys.Select(company =>
        {
            var revenue = revenueByCompany[company];
            var previousTotal = previousTotalByCompany.GetValueOrDefault(company);
            var markupUsd = revenue - previousTotal;
            var markupPercent = previousTotal == 0 ? 0 : markupUsd / previousTotal * 100;
            return new OffshoreCompanyAccumulated(company, revenue, markupUsd, markupPercent);
        }).OrderByDescending(r => r.AccumulatedMarkupUsd).ToList();

        return Ok(result);
    }
}
