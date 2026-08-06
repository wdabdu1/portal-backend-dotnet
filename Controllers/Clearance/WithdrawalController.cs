using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Clearance;

public record CreateWithdrawalRequest(int DepositShipmentId);
public record WithdrawalSummary(int Id, int DepositShipmentId, string DepositBlAwbNo, DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo);

public record WithdrawalGeneralInfoRequest(DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo);

public record WithdrawalProcessingRequest(
    DateOnly? CertificateEntryDate, string? ScudaDeclarationNo,
    DateOnly? SsmoFileRequestDate, decimal? SsmoInspectionAmountSdg, DateOnly? SsmoFeesSettlementDate,
    DateOnly? CustExamStartDate, DateOnly? CustExamCompletedDate,
    bool CustomsLabRequired, decimal? CustomsLabFeesSdg, DateOnly? LabFeesPaymentDate, DateOnly? LabResultIssuanceDate,
    DateOnly? SsmoExamStartDate, DateOnly? SsmoCertIssuanceDate,
    DateOnly? CustEvaluationDate, decimal? CustomsDutySdg, DateOnly? CustomsSettlementDate, DateOnly? ReleaseExitPassDate,
    DateOnly? TruckPortEntryPermitDate, DateOnly? ClearanceActualCompletedDate);

public record WithdrawalLineItemInput(int ShipmentLineItemId, decimal Qty);
public record SaveWithdrawalLineItemsRequest(List<WithdrawalLineItemInput> Lines);

public record WithdrawalDetailResponse(
    int Id, int DepositShipmentId, string DepositBlAwbNo,
    DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo,
    DateOnly? CertificateEntryDate, string? ScudaDeclarationNo,
    DateOnly? SsmoFileRequestDate, decimal? SsmoInspectionAmountSdg, DateOnly? SsmoFeesSettlementDate,
    DateOnly? CustExamStartDate, DateOnly? CustExamCompletedDate,
    bool CustomsLabRequired, decimal? CustomsLabFeesSdg, DateOnly? LabFeesPaymentDate, DateOnly? LabResultIssuanceDate,
    DateOnly? SsmoExamStartDate, DateOnly? SsmoCertIssuanceDate,
    DateOnly? CustEvaluationDate, decimal? CustomsDutySdg, DateOnly? CustomsSettlementDate, DateOnly? ReleaseExitPassDate,
    DateOnly? TruckPortEntryPermitDate, DateOnly? ClearanceActualCompletedDate);

[ApiController]
[Authorize]
[Route("api/withdrawals")]
public class WithdrawalController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public WithdrawalController(ShippingPortalDbContext db) => _db = db;

    [HttpPost]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<ActionResult<WithdrawalSummary>> Create(CreateWithdrawalRequest req)
    {
        var deposit = await _db.Shipments.FindAsync(req.DepositShipmentId);
        if (deposit is null) return NotFound(new { message = "Deposit shipment not found." });

        var withdrawal = new Withdrawal { DepositShipmentId = req.DepositShipmentId };
        _db.Withdrawals.Add(withdrawal);
        await _db.SaveChangesAsync();

        return Ok(new WithdrawalSummary(withdrawal.Id, deposit.Id, deposit.BlAwbNo, null, null));
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<WithdrawalSummary>>> GetAll([FromQuery] int? depositShipmentId)
    {
        var query = _db.Withdrawals.Include(w => w.DepositShipment).AsQueryable();
        if (depositShipmentId.HasValue) query = query.Where(w => w.DepositShipmentId == depositShipmentId.Value);

        return Ok(await query
            .OrderByDescending(w => w.Id)
            .Select(w => new WithdrawalSummary(w.Id, w.DepositShipmentId, w.DepositShipment!.BlAwbNo, w.WithdrawalRequestDate, w.WithdrawalRequestRefNo))
            .ToListAsync());
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<WithdrawalDetailResponse>> GetDetail(int id)
    {
        var w = await _db.Withdrawals.Include(x => x.DepositShipment).FirstOrDefaultAsync(x => x.Id == id);
        if (w is null) return NotFound();

        return Ok(new WithdrawalDetailResponse(
            w.Id, w.DepositShipmentId, w.DepositShipment!.BlAwbNo,
            w.WithdrawalRequestDate, w.WithdrawalRequestRefNo,
            w.CertificateEntryDate, w.ScudaDeclarationNo,
            w.SsmoFileRequestDate, w.SsmoInspectionAmountSdg, w.SsmoFeesSettlementDate,
            w.CustExamStartDate, w.CustExamCompletedDate,
            w.CustomsLabRequired, w.CustomsLabFeesSdg, w.LabFeesPaymentDate, w.LabResultIssuanceDate,
            w.SsmoExamStartDate, w.SsmoCertIssuanceDate,
            w.CustEvaluationDate, w.CustomsDutySdg, w.CustomsSettlementDate, w.ReleaseExitPassDate,
            w.TruckPortEntryPermitDate, w.ClearanceActualCompletedDate));
    }

    [HttpPut("{id:int}/general-info")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> SaveGeneralInfo(int id, WithdrawalGeneralInfoRequest req)
    {
        var w = await _db.Withdrawals.FindAsync(id);
        if (w is null) return NotFound();

        w.WithdrawalRequestDate = req.WithdrawalRequestDate;
        w.WithdrawalRequestRefNo = req.WithdrawalRequestRefNo;
        w.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}/processing")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> SaveProcessing(int id, WithdrawalProcessingRequest req)
    {
        var w = await _db.Withdrawals.FindAsync(id);
        if (w is null) return NotFound();

        w.CertificateEntryDate = req.CertificateEntryDate;
        w.ScudaDeclarationNo = req.ScudaDeclarationNo;
        w.SsmoFileRequestDate = req.SsmoFileRequestDate;
        w.SsmoInspectionAmountSdg = req.SsmoInspectionAmountSdg;
        w.SsmoFeesSettlementDate = req.SsmoFeesSettlementDate;
        w.CustExamStartDate = req.CustExamStartDate;
        w.CustExamCompletedDate = req.CustExamCompletedDate;
        w.CustomsLabRequired = req.CustomsLabRequired;
        w.CustomsLabFeesSdg = req.CustomsLabFeesSdg;
        w.LabFeesPaymentDate = req.LabFeesPaymentDate;
        w.LabResultIssuanceDate = req.LabResultIssuanceDate;
        w.SsmoExamStartDate = req.SsmoExamStartDate;
        w.SsmoCertIssuanceDate = req.SsmoCertIssuanceDate;
        w.CustEvaluationDate = req.CustEvaluationDate;
        w.CustomsDutySdg = req.CustomsDutySdg;
        w.CustomsSettlementDate = req.CustomsSettlementDate;
        w.ReleaseExitPassDate = req.ReleaseExitPassDate;
        w.TruckPortEntryPermitDate = req.TruckPortEntryPermitDate;
        w.ClearanceActualCompletedDate = req.ClearanceActualCompletedDate;
        w.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/cost-estimate")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<object>> GetCostEstimate(int id)
    {
        var estimate = await _db.WithdrawalCostEstimates.FirstOrDefaultAsync(x => x.WithdrawalId == id);
        var lineItems = await _db.WithdrawalEstimateLineItems
            .Where(x => x.WithdrawalId == id)
            .Include(x => x.ChargeType)
            .ToListAsync();

        return Ok(new
        {
            estimate,
            totalSdg = lineItems.Sum(x => x.ValueSdg),
            lineItems = lineItems.Select(x => new EstimateLineItemResponse(x.Id, x.ChargeTypeId, x.ChargeType!.Name, x.ValueSdg, x.DueDate))
        });
    }

    [HttpPut("{id:int}/cost-estimate")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> SaveCostEstimate(int id, CostEstimateRequest req)
    {
        if (!await _db.Withdrawals.AnyAsync(w => w.Id == id)) return NotFound();

        var entity = await _db.WithdrawalCostEstimates.FirstOrDefaultAsync(x => x.WithdrawalId == id);
        if (entity is null) { entity = new WithdrawalCostEstimate { WithdrawalId = id }; _db.WithdrawalCostEstimates.Add(entity); }

        entity.EstimateDate = req.EstimateDate;
        entity.NotifyBuDate = req.NotifyBuDate;
        entity.AmountSettledDate = req.AmountSettledDate;
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPost("{id:int}/estimate-line-items")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<ActionResult<EstimateLineItemResponse>> AddEstimateLineItem(int id, EstimateLineItemRequest req)
    {
        if (!await _db.Withdrawals.AnyAsync(w => w.Id == id)) return NotFound();

        var entity = new WithdrawalEstimateLineItem { WithdrawalId = id, ChargeTypeId = req.ChargeTypeId, ValueSdg = req.ValueSdg, DueDate = req.DueDate };
        _db.WithdrawalEstimateLineItems.Add(entity);
        await _db.SaveChangesAsync();

        var chargeType = await _db.ClearanceChargeTypes.FindAsync(req.ChargeTypeId);
        return Ok(new EstimateLineItemResponse(entity.Id, entity.ChargeTypeId, chargeType?.Name ?? "", entity.ValueSdg, entity.DueDate));
    }

    [HttpDelete("{id:int}/estimate-line-items/{lineItemId:int}")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> DeleteEstimateLineItem(int id, int lineItemId)
    {
        var entity = await _db.WithdrawalEstimateLineItems.FirstOrDefaultAsync(x => x.Id == lineItemId && x.WithdrawalId == id);
        if (entity is null) return NotFound();

        _db.WithdrawalEstimateLineItems.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/line-items")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<FzBalanceLine>>> GetLineItems(int id)
    {
        var w = await _db.Withdrawals.FindAsync(id);
        if (w is null) return NotFound();

        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == w.DepositShipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .ToListAsync();

        var withdrawnByLineItem = await _db.WithdrawalLineItems
            .Where(x => lineItems.Select(li => li.Id).Contains(x.DepositShipmentLineItemId))
            .GroupBy(x => x.DepositShipmentLineItemId)
            .Select(g => new { LineItemId = g.Key, Total = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.LineItemId, x => x.Total);

        var thisWithdrawalQty = await _db.WithdrawalLineItems
            .Where(x => x.WithdrawalId == id)
            .ToDictionaryAsync(x => x.DepositShipmentLineItemId, x => x.Qty);

        var result = lineItems.Select(li =>
        {
            var withdrawnElsewhere = withdrawnByLineItem.GetValueOrDefault(li.Id, 0m) - thisWithdrawalQty.GetValueOrDefault(li.Id, 0m);
            var availableForThisWithdrawal = li.QtyInBl - withdrawnElsewhere;
            return new FzBalanceLine(li.Id, li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "", li.QtyInBl, withdrawnElsewhere, availableForThisWithdrawal);
        }).Where(l => l.Balance > 0 || thisWithdrawalQty.ContainsKey(l.ShipmentLineItemId)).ToList();

        return Ok(result);
    }

    [HttpPut("{id:int}/line-items")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> SaveLineItems(int id, SaveWithdrawalLineItemsRequest req)
    {
        if (!await _db.Withdrawals.AnyAsync(w => w.Id == id)) return NotFound();

        var existing = await _db.WithdrawalLineItems.Where(x => x.WithdrawalId == id).ToListAsync();
        _db.WithdrawalLineItems.RemoveRange(existing);

        foreach (var line in req.Lines.Where(l => l.Qty > 0))
        {
            _db.WithdrawalLineItems.Add(new WithdrawalLineItem { WithdrawalId = id, DepositShipmentLineItemId = line.ShipmentLineItemId, Qty = line.Qty });
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
