using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ClearanceEntity = ShippingPortal.Api.Models.Clearance.Clearance;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Controllers.Clearance;

public record Route1Request(
    DateOnly? MoveRequestDate, decimal? BillAmountSdg, DateOnly? BillSettlementDate,
    DateOnly? SsmoFileRequestDate, decimal? SsmoInspectionAmountSdg, DateOnly? SsmoFeesSettlementDate,
    DateOnly? CustExamStartDate, DateOnly? CustExamCompletedDate,
    bool CustomsLabRequired, decimal? CustomsLabFeesSdg, DateOnly? LabFeesPaymentDate, DateOnly? LabResultIssuanceDate,
    DateOnly? SsmoExamStartDate, DateOnly? SsmoCertIssuanceDate,
    DateOnly? CustEvaluationDate, decimal? CustomsDutySdg, DateOnly? CustomsSettlementDate, DateOnly? ReleaseExitPassDate,
    DateOnly? SpcBillRequestDate, decimal? SpcBillValueSdg, DateOnly? SpcBillSettlementDate,
    DateOnly? TruckPortEntryPermitDate, DateOnly? ContainersReturnedDate, DateOnly? ClearanceActualCompletedDate);

public record Route2Request(
    DateOnly? DepositRequestDate, DateOnly? RequestApprovalDate,
    DateOnly? InspectionDate,
    DateOnly? SpcBillRequestDate, decimal? SpcBillValueSdg, DateOnly? SpcBillSettlementDate, DateOnly? PoliceSecurityAppointedDate,
    DateOnly? TruckPortEntryPermitDate, DateOnly? ContainersReceivedAtFzDate, DateOnly? ContainersReturnedDate, DateOnly? ClearanceActualCompletedDate);

public record Route3Request(
    DateOnly? CertificateEntryDate, string? ScudaDeclarationNo,
    DateOnly? SsmoFileRequestDate, decimal? SsmoInspectionAmountSdg, DateOnly? SsmoFeesSettlementDate,
    DateOnly? CustExamStartDate, DateOnly? CustExamCompletedDate,
    bool CustomsLabRequired, decimal? CustomsLabFeesSdg, DateOnly? LabFeesPaymentDate, DateOnly? LabResultIssuanceDate,
    DateOnly? SsmoExamStartDate, DateOnly? SsmoCertIssuanceDate,
    DateOnly? CustEvaluationDate, decimal? CustomsDutySdg, DateOnly? CustomsSettlementDate, DateOnly? ReleaseExitPassDate,
    DateOnly? TruckPortEntryPermitDate, DateOnly? ClearanceActualCompletedDate);

[ApiController]
[Authorize]
[Route("api/clearance/{shipmentId:int}")]
public class ClearanceRouteDetailsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceRouteDetailsController(ShippingPortalDbContext db) => _db = db;

    private async Task<ClearanceEntity?> GetOrCreateClearanceAsync(int shipmentId)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return null;

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null)
        {
            clearance = new ClearanceEntity { ShipmentId = shipmentId };
            _db.Clearances.Add(clearance);
            await _db.SaveChangesAsync();
        }
        return clearance;
    }

    [HttpGet("route1")]
    public async Task<ActionResult<ClearanceRoute1Details>> GetRoute1(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        var details = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        return Ok(details);
    }

    [HttpPut("route1")]
    public async Task<IActionResult> SaveRoute1(int shipmentId, Route1Request req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceRoute1Details { ClearanceId = clearance.Id }; _db.ClearanceRoute1Details.Add(entity); }

        entity.MoveRequestDate = req.MoveRequestDate;
        entity.BillAmountSdg = req.BillAmountSdg;
        entity.BillSettlementDate = req.BillSettlementDate;
        entity.SsmoFileRequestDate = req.SsmoFileRequestDate;
        entity.SsmoInspectionAmountSdg = req.SsmoInspectionAmountSdg;
        entity.SsmoFeesSettlementDate = req.SsmoFeesSettlementDate;
        entity.CustExamStartDate = req.CustExamStartDate;
        entity.CustExamCompletedDate = req.CustExamCompletedDate;
        entity.CustomsLabRequired = req.CustomsLabRequired;
        entity.CustomsLabFeesSdg = req.CustomsLabFeesSdg;
        entity.LabFeesPaymentDate = req.LabFeesPaymentDate;
        entity.LabResultIssuanceDate = req.LabResultIssuanceDate;
        entity.SsmoExamStartDate = req.SsmoExamStartDate;
        entity.SsmoCertIssuanceDate = req.SsmoCertIssuanceDate;
        entity.CustEvaluationDate = req.CustEvaluationDate;
        entity.CustomsDutySdg = req.CustomsDutySdg;
        entity.CustomsSettlementDate = req.CustomsSettlementDate;
        entity.ReleaseExitPassDate = req.ReleaseExitPassDate;
        entity.SpcBillRequestDate = req.SpcBillRequestDate;
        entity.SpcBillValueSdg = req.SpcBillValueSdg;
        entity.SpcBillSettlementDate = req.SpcBillSettlementDate;
        entity.TruckPortEntryPermitDate = req.TruckPortEntryPermitDate;
        entity.ContainersReturnedDate = req.ContainersReturnedDate;
        entity.ClearanceActualCompletedDate = req.ClearanceActualCompletedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("route2")]
    public async Task<ActionResult<ClearanceRoute2Details>> GetRoute2(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        var details = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        return Ok(details);
    }

    [HttpPut("route2")]
    public async Task<IActionResult> SaveRoute2(int shipmentId, Route2Request req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceRoute2Details { ClearanceId = clearance.Id }; _db.ClearanceRoute2Details.Add(entity); }

        entity.DepositRequestDate = req.DepositRequestDate;
        entity.RequestApprovalDate = req.RequestApprovalDate;
        entity.InspectionDate = req.InspectionDate;
        entity.SpcBillRequestDate = req.SpcBillRequestDate;
        entity.SpcBillValueSdg = req.SpcBillValueSdg;
        entity.SpcBillSettlementDate = req.SpcBillSettlementDate;
        entity.PoliceSecurityAppointedDate = req.PoliceSecurityAppointedDate;
        entity.TruckPortEntryPermitDate = req.TruckPortEntryPermitDate;
        entity.ContainersReceivedAtFzDate = req.ContainersReceivedAtFzDate;
        entity.ContainersReturnedDate = req.ContainersReturnedDate;
        entity.ClearanceActualCompletedDate = req.ClearanceActualCompletedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("route3")]
    public async Task<ActionResult<ClearanceRoute3Details>> GetRoute3(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        var details = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        return Ok(details);
    }

    [HttpPut("route3")]
    public async Task<IActionResult> SaveRoute3(int shipmentId, Route3Request req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceRoute3Details { ClearanceId = clearance.Id }; _db.ClearanceRoute3Details.Add(entity); }

        entity.CertificateEntryDate = req.CertificateEntryDate;
        entity.ScudaDeclarationNo = req.ScudaDeclarationNo;
        entity.SsmoFileRequestDate = req.SsmoFileRequestDate;
        entity.SsmoInspectionAmountSdg = req.SsmoInspectionAmountSdg;
        entity.SsmoFeesSettlementDate = req.SsmoFeesSettlementDate;
        entity.CustExamStartDate = req.CustExamStartDate;
        entity.CustExamCompletedDate = req.CustExamCompletedDate;
        entity.CustomsLabRequired = req.CustomsLabRequired;
        entity.CustomsLabFeesSdg = req.CustomsLabFeesSdg;
        entity.LabFeesPaymentDate = req.LabFeesPaymentDate;
        entity.LabResultIssuanceDate = req.LabResultIssuanceDate;
        entity.SsmoExamStartDate = req.SsmoExamStartDate;
        entity.SsmoCertIssuanceDate = req.SsmoCertIssuanceDate;
        entity.CustEvaluationDate = req.CustEvaluationDate;
        entity.CustomsDutySdg = req.CustomsDutySdg;
        entity.CustomsSettlementDate = req.CustomsSettlementDate;
        entity.ReleaseExitPassDate = req.ReleaseExitPassDate;
        entity.TruckPortEntryPermitDate = req.TruckPortEntryPermitDate;
        entity.ClearanceActualCompletedDate = req.ClearanceActualCompletedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }
}
