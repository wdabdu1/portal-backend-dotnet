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
public record ShipmentSupplierPaymentRequest(
    int? CurrencyId,
    decimal? AdvanceValue, DateOnly? AdvanceDueDate, DateOnly? AdvanceActualPaymentDate,
    decimal? RemainingValue, DateOnly? RemainingDueDate, DateOnly? RemainingActualPaymentDate,
    string? Remarks);public record ShipmentBankingRequest(
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
    public async Task<IActionResult> UpsertMot(int shipmentId, ShipmentMotRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentMot { ShipmentId = shipmentId }; _db.ShipmentMots.Add(entity); }

        entity.ProcessDate = req.ProcessDate;
        entity.Cost = req.Cost;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;
        entity.OffshoreApprovedPiNumber = req.OffshoreApprovedPiNumber;
        entity.OffshoreApprovedPiDate = req.OffshoreApprovedPiDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("supplier-full-set")]
    public async Task<IActionResult> UpsertSupplierFullSet(int shipmentId, ShipmentSupplierFullSetRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentSupplierFullSet { ShipmentId = shipmentId }; _db.ShipmentSupplierFullSets.Add(entity); }

        entity.SupplierInvoiceNo = req.SupplierInvoiceNo;
        entity.SupplierInvoiceDate = req.SupplierInvoiceDate;
        entity.FsDispatchDate = req.FsDispatchDate;
        entity.FsDispatchedViaId = req.FsDispatchedViaId;
        entity.FsTrackingNumber = req.FsTrackingNumber;
        entity.FsReceivedDate = req.FsReceivedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("supplier-payment")]
    public async Task<IActionResult> UpsertSupplierPayment(int shipmentId, ShipmentSupplierPaymentRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentSupplierPayments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentSupplierPayment { ShipmentId = shipmentId }; _db.ShipmentSupplierPayments.Add(entity); }

        entity.DueDate = req.DueDate;
        entity.DueAmount = req.DueAmount;
        entity.CurrencyId = req.CurrencyId;
        entity.PaymentExecutedDate = req.PaymentExecutedDate;
        entity.PaymentExecutedValue = req.PaymentExecutedValue;
        entity.PaymentExecutedCurrencyId = req.PaymentExecutedCurrencyId;
        entity.Remarks = req.Remarks;

        if (req.DueAmount.HasValue && req.CurrencyId.HasValue)
        {
            var rate = await _db.FxRates.Where(r => r.CurrencyId == req.CurrencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
            entity.DueAmountUsd = req.DueAmount.Value / (rate?.RateToUsd ?? 1m);
        }

        if (req.PaymentExecutedValue.HasValue && req.PaymentExecutedCurrencyId.HasValue)
        {
            var rate = await _db.FxRates.Where(r => r.CurrencyId == req.PaymentExecutedCurrencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
            entity.PaymentExecutedUsd = req.PaymentExecutedValue.Value / (rate?.RateToUsd ?? 1m);
        }

        entity.DueBalanceUsd = (entity.DueAmountUsd ?? 0) - (entity.PaymentExecutedUsd ?? 0);

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("banking")]
    public async Task<IActionResult> UpsertBanking(int shipmentId, ShipmentBankingRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentBankings.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentBanking { ShipmentId = shipmentId }; _db.ShipmentBankings.Add(entity); }

        entity.SenderBankId = req.SenderBankId;
        entity.OsDocDispatchDate = req.OsDocDispatchDate;
        entity.OsDocDispatchedViaId = req.OsDocDispatchedViaId;
        entity.OsDocTrackingNumber = req.OsDocTrackingNumber;
        entity.ReceivingBankId = req.ReceivingBankId;
        entity.NecessaryGoodType = req.NecessaryGoodType;
        entity.CollectionRefNo = req.CollectionRefNo;
        entity.CollectionValue = req.CollectionValue;
        entity.CollectionCurrencyId = req.CollectionCurrencyId;
        entity.TenorId = req.TenorId;
        entity.CollectionDueDate = req.CollectionDueDate;
        entity.CollectionAmountSettled = req.CollectionAmountSettled;
        entity.ImFormNo = req.ImFormNo;
        entity.ImFormDate = req.ImFormDate;

        if (req.CollectionValue.HasValue && req.SenderBankId.HasValue)
        {
            var senderBank = await _db.SenderBanks.FindAsync(req.SenderBankId);
            if (senderBank is not null)
                entity.SenderBankCharges = Math.Max(req.CollectionValue.Value * senderBank.ChargeRate, senderBank.MinimumChargeAed);
        }

        if (req.CollectionValue.HasValue && req.ReceivingBankId.HasValue)
        {
            var receiverBank = await _db.ReceiverBanks.FindAsync(req.ReceivingBankId);
            if (receiverBank is not null)
                entity.ReceiverBankCharges = req.CollectionValue.Value * receiverBank.TotalChargeRate;
        }

        if (req.CollectionValue.HasValue)
            entity.RemainingDues = req.CollectionValue.Value - (req.CollectionAmountSettled ?? 0);

        await _db.SaveChangesAsync();
        return Ok(entity);
    }
}
