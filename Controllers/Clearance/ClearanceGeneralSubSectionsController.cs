using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ClearanceEntity = ShippingPortal.Api.Models.Clearance.Clearance;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Controllers.Clearance;

public record DeliveryOrderRequest(
    DateOnly? CopyOfDoCollectedDate, DateOnly? ReceiveDoDate, DateOnly? ActualArrivalDate,
    decimal? DoFeesSdg, DateOnly? DoFeesSettledDate, DateOnly? DoReceivedDate);

public record CostEstimateRequest(
    DateOnly? EstimateDate, decimal? EstimateValueSdg, DateOnly? NotifyBuDate, DateOnly? AmountSettledDate);

public record CertificateEntryRequest(DateOnly? CertificateEntryDate, string? ScudaDeclarationNo);

[ApiController]
[Authorize]
[Route("api/clearance/{shipmentId:int}")]
public class ClearanceGeneralSubSectionsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceGeneralSubSectionsController(ShippingPortalDbContext db) => _db = db;

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

    [HttpGet("delivery-order")]
    public async Task<ActionResult<ClearanceDeliveryOrder>> GetDeliveryOrder(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        return Ok(await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id));
    }

    [HttpPut("delivery-order")]
    public async Task<IActionResult> SaveDeliveryOrder(int shipmentId, DeliveryOrderRequest req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceDeliveryOrder { ClearanceId = clearance.Id }; _db.ClearanceDeliveryOrders.Add(entity); }

        entity.CopyOfDoCollectedDate = req.CopyOfDoCollectedDate;
        entity.ReceiveDoDate = req.ReceiveDoDate;
        entity.ActualArrivalDate = req.ActualArrivalDate;
        entity.DoFeesSdg = req.DoFeesSdg;
        entity.DoFeesSettledDate = req.DoFeesSettledDate;
        entity.DoReceivedDate = req.DoReceivedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("cost-estimate")]
    public async Task<ActionResult<ClearanceCostEstimate>> GetCostEstimate(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        return Ok(await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id));
    }

    [HttpPut("cost-estimate")]
    public async Task<IActionResult> SaveCostEstimate(int shipmentId, CostEstimateRequest req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceCostEstimate { ClearanceId = clearance.Id }; _db.ClearanceCostEstimates.Add(entity); }

        entity.EstimateDate = req.EstimateDate;
        entity.EstimateValueSdg = req.EstimateValueSdg;
        entity.NotifyBuDate = req.NotifyBuDate;
        entity.AmountSettledDate = req.AmountSettledDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("certificate-entry")]
    public async Task<ActionResult<ClearanceCertificateEntry>> GetCertificateEntry(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(null);
        return Ok(await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id));
    }

    [HttpPut("certificate-entry")]
    public async Task<IActionResult> SaveCertificateEntry(int shipmentId, CertificateEntryRequest req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceCertificateEntry { ClearanceId = clearance.Id }; _db.ClearanceCertificateEntries.Add(entity); }

        entity.CertificateEntryDate = req.CertificateEntryDate;
        entity.ScudaDeclarationNo = req.ScudaDeclarationNo;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }
}
