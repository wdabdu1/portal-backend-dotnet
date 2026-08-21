using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

// A dedicated controller for the "group settlement letter" workflow —
// distinct from BankDuesController (the general open-dues list), since
// this needs Receiver+Sender Bank filtering and a few extra fields
// (Collection Ref. No., Sender Bank) that the general list doesn't
// expose, plus the confirm/generate-letter action itself.
public record PayableDueRow(
    int ShipmentId, string BlAwbNo, string Category, string? InvoiceNo, string? CollectionRefNo,
    decimal ValueAed, decimal PaidAed, decimal RemainingAed);

public record SenderBankOption(int Id, string Name);

[ApiController]
[Authorize(Roles = AppRoles.BankDuesViewers)]
[Route("api/pay-bank-dues")]
public class PayBankDuesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly BuAccessService _buAccess;
    public PayBankDuesController(ShippingPortalDbContext db, BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

    private readonly Dictionary<int, decimal> _fxCache = new();

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        if (_fxCache.TryGetValue(currencyId.Value, out var cached)) return cached;

        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var value = rate?.RateToUsd ?? 1m;
        _fxCache[currencyId.Value] = value;
        return value;
    }

    private async Task<decimal> ConvertToAedAsync(decimal value, int? currencyId)
    {
        var sourceRate = await GetFxRateAsync(currencyId);
        var aedCurrency = await _db.Currencies.FirstOrDefaultAsync(c => c.Code == "AED");
        var aedRate = aedCurrency is null ? 1m : await GetFxRateAsync(aedCurrency.Id);
        return (value / sourceRate) * aedRate;
    }

    // Every ShipmentBanking row with an outstanding balance, for the
    // given Receiver Bank — used to populate the Sender Bank sub-filter
    // with only options that genuinely have something payable.
    [HttpGet("sender-banks")]
    public async Task<ActionResult<IEnumerable<SenderBankOption>>> GetSenderBankOptions([FromQuery] int receiverBankId, [FromServices] BuAccessService buAccess)
    {
        var rows = await GetOutstandingRowsAsync(receiverBankId, senderBankId: null, buAccess);
        return Ok(rows
            .Where(r => r.Banking.SenderBankId.HasValue)
            .Select(r => new SenderBankOption(r.Banking.SenderBankId!.Value, r.Banking.SenderBank!.Name))
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.Name)
            .ToList());
    }

    [HttpGet("dues")]
    public async Task<ActionResult<IEnumerable<PayableDueRow>>> GetDues([FromQuery] int receiverBankId, [FromQuery] int senderBankId, [FromServices] BuAccessService buAccess)
    {
        var rows = await GetOutstandingRowsAsync(receiverBankId, senderBankId, buAccess);
        return Ok(rows.Select(r => r.Row).OrderBy(r => r.BlAwbNo).ToList());
    }

    private async Task<List<(PayableDueRow Row, ShipmentBanking Banking)>> GetOutstandingRowsAsync(int receiverBankId, int? senderBankId, BuAccessService buAccess)
    {
        var query = _db.ShipmentBankings
            .Where(b => b.ReceivingBankId == receiverBankId)
            .Include(b => b.Shipment!).ThenInclude(s => s.PurchaseOrder)
            .Include(b => b.SenderBank)
            .Include(b => b.CollectionCurrency)
            .AsQueryable();

        if (senderBankId.HasValue)
            query = query.Where(b => b.SenderBankId == senderBankId);

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(b => allowedBus.Contains(b.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var bankings = await query.ToListAsync();

        var lastOffshoreInvoicesByShipment = await _db.LastOffshoreDetails.ToDictionaryAsync(d => d.ShipmentId, d => d.InvoiceNo);
        var shipmentIds = bankings.Select(b => b.ShipmentId).ToList();
        var categoriesByShipment = await _db.ShipmentLineItems
            .Where(li => shipmentIds.Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .GroupBy(li => li.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, Category = g.First().PurchaseOrderLineItem!.ProductCategory!.Name })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Category);

        var collectionsByShipment = new Dictionary<int, List<ShipmentCollectionRecord>>();
        foreach (var record in await _db.ShipmentCollectionRecords.Where(r => shipmentIds.Contains(r.ShipmentId)).ToListAsync())
        {
            if (!collectionsByShipment.TryGetValue(record.ShipmentId, out var list))
            {
                list = new List<ShipmentCollectionRecord>();
                collectionsByShipment[record.ShipmentId] = list;
            }
            list.Add(record);
        }

        var results = new List<(PayableDueRow, ShipmentBanking)>();
        foreach (var banking in bankings)
        {
            if (!banking.CollectionValue.HasValue) continue;
            var shipment = banking.Shipment!;

            var valueAed = await ConvertToAedAsync(banking.CollectionValue.Value, banking.CollectionCurrencyId);
            decimal paidAed = 0;
            if (collectionsByShipment.TryGetValue(shipment.Id, out var records))
                foreach (var r in records) paidAed += await ConvertToAedAsync(r.Value, r.CurrencyId);

            var remainingAed = valueAed - paidAed;
            if (remainingAed <= 0) continue;

            var row = new PayableDueRow(
                shipment.Id, shipment.BlAwbNo, categoriesByShipment.GetValueOrDefault(shipment.Id, ""),
                lastOffshoreInvoicesByShipment.GetValueOrDefault(shipment.Id), banking.CollectionRefNo,
                valueAed, paidAed, remainingAed);

            results.Add((row, banking));
        }
        return results;
    }
}
