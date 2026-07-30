using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ShipmentForwarderRequest(int? ForwarderId, decimal? ActualShippingCost, int? CurrencyId, decimal? AmountSaved, bool MarineInsurance);
public record ShipmentAcdRequest(DateOnly? ProcessDate, decimal? CostUsd, DateOnly? CostSettledDate, string? RefNumber);
public record ShipmentDraftDocumentsRequest(DateOnly? InitialDraftReceivedDate, DateOnly? FinalDraftReceivedDate, DateOnly? FinalDraftConfirmedDate);
public record ShipmentSsmoRequest(DateOnly? ApplicationDate, decimal? Cost, DateOnly? CostSettledDate, string? RefNumber);
public record ShipmentMotRequest(DateOnly? ProcessDate, decimal? Cost, DateOnly? CostSettledDate, string? RefNumber, string? OffshoreApprovedPiNumber, DateOnly? OffshoreApprovedPiDate);
public record ShipmentSupplierFullSetRequest(string? SupplierInvoiceNo, DateOnly? SupplierInvoiceDate, DateOnly? FsDispatchDate, int? FsDispatchedViaId, string? FsTrackingNumber, DateOnly? FsReceivedDate);
public record ShipmentSupplierPaymentRequest(DateOnly? DueDate, decimal? DueAmount, int? CurrencyId, DateOnly? PaymentExecutedDate, decimal? PaymentExecutedValue, int? PaymentExecutedCurrencyId, string? Remarks);
public record ShipmentBankingRequest(
    int? SenderBankId, DateOnly? OsDocDispatchDate, int? OsDocDispatchedViaId, string? OsDocTrackingNumber,
    int? ReceivingBankId, bool NecessaryGoodType, string? CollectionRefNo, decimal? CollectionValue, int? CollectionCurrencyId,
    int? TenorId, DateOnly? CollectionDueDate, decimal? CollectionAmountSettled, string? ImFormNo, DateOnly? ImFormDate);

public record ShipmentDetailResponse(
    int Id, string BlAwbNo, string PoNumber, string Status,
    ShipmentForwarder? Forwarder, ShipmentAcd? Acd, ShipmentDraftDocuments? DraftDocuments,
    ShipmentSsmo? Ssmo, ShipmentMot? Mot, ShipmentSupplierFullSet? SupplierFullSet,
    ShipmentSupplierPayment? SupplierPayment, ShipmentBanking? Banking);

[ApiController]
[Authorize]
[Route("api/shipments/{shipmentId:int}")]
public class ShipmentDetailController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShipmentDetailController(ShippingPortalDbContext db) => _db = db;

    [HttpGet("detail")]
    public async Task<ActionResult<ShipmentDetailResponse>> GetDetail(int shipmentId)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var forwarder = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var draftDocs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var payment = await _db.ShipmentSupplierPayments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);

        return new ShipmentDetailResponse(shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Status.ToString(),
            forwarder, acd, draftDocs, ssmo, mot, fullSet, payment, banking);
    }

    [HttpPut("forwarder")]
    public async Task<IActionResult> UpsertForwarder(int shipmentId, ShipmentForwarderRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentForwarder { ShipmentId = shipmentId }; _db.ShipmentForwarders.Add(entity); }

        entity.ForwarderId = req.ForwarderId;
        entity.ActualShippingCost = req.ActualShippingCost;
        entity.CurrencyId = req.CurrencyId;
        entity.AmountSaved = req.AmountSaved;
        entity.MarineInsurance = req.MarineInsurance;

        if (req.ActualShippingCost.HasValue && req.CurrencyId.HasValue)
        {
            var rate = await _db.FxRates.Where(r => r.CurrencyId == req.CurrencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
            entity.ActualShippingCostUsd = req.ActualShippingCost.Value / (rate?.RateToUsd ?? 1m);
        }

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("acd")]
    public async Task<IActionResult> UpsertAcd(int shipmentId, ShipmentAcdRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentAcd { ShipmentId = shipmentId }; _db.ShipmentAcds.Add(entity); }

        entity.ProcessDate = req.ProcessDate;
        entity.CostUsd = req.CostUsd;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("draft-documents")]
    public async Task<IActionResult> UpsertDraftDocuments(int shipmentId, ShipmentDraftDocumentsRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentDraftDocuments { ShipmentId = shipmentId }; _db.ShipmentDraftDocuments.Add(entity); }

        entity.InitialDraftReceivedDate = req.InitialDraftReceivedDate;
        entity.FinalDraftReceivedDate = req.FinalDraftReceivedDate;
        entity.FinalDraftConfirmedDate = req.FinalDraftConfirmedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("ssmo")]
    public async Task<IActionResult> UpsertSsmo(int shipmentId, ShipmentSsmoRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentSsmo { ShipmentId = shipmentId }; _db.ShipmentSsmos.Add(entity); }

        entity.ApplicationDate = req.ApplicationDate;
        entity.Cost = req.Cost;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("mot")]
    public async Task<IActionResult> UpsertMot(int
