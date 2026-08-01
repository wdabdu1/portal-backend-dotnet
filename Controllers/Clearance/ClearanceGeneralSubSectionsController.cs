using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ClearanceEntity = ShippingPortal.Api.Models.Clearance.Clearance;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Controllers.Clearance;

public record DeliveryOrderRequest(
    DateOnly? CopyOfDoCollectedDate, DateOnly? ReceiveDoDate, DateOnly? ActualArrivalDate,
    bool DepositRequired, decimal? DoActualFeesSdg, DateOnly? DoFeesSettledDate, DateOnly? DoReceivedDate);

public record CostEstimateRequest(DateOnly? EstimateDate, DateOnly? NotifyBuDate, DateOnly? AmountSettledDate);

public record EstimateLineItemRequest(int ChargeTypeId, decimal ValueSdg, DateOnly? DueDate);
public record EstimateLineItemResponse(int Id, int ChargeTypeId, string ChargeTypeName, decimal ValueSdg, DateOnly? DueDate);

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
        entity.DepositRequired = req.DepositRequired;
        entity.DoActualFeesSdg = req.DoActualFeesSdg;
        entity.DoFeesSettledDate = req.DoFeesSettledDate;
        entity.DoReceivedDate = req.DoReceivedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("cost-estimate")]
    public async Task<ActionResult<object>> GetCostEstimate(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(new { estimate = (ClearanceCostEstimate?)null, totalSdg = 0m });

        var entity = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        var total = await _db.ClearanceEstimateLineItems.Where(x => x.ClearanceId == clearance.Id).SumAsync(x => (decimal?)x.ValueSdg) ?? 0m;

        return Ok(new { estimate = entity, totalSdg = total });
    }

    [HttpPut("cost-estimate")]
    public async Task<IActionResult> SaveCostEstimate(int shipmentId, CostEstimateRequest req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
        if (entity is null) { entity = new ClearanceCostEstimate { ClearanceId = clearance.Id }; _db.ClearanceCostEstimates.Add(entity); }

        entity.EstimateDate = req.EstimateDate;
        entity.NotifyBuDate = req.NotifyBuDate;
        entity.AmountSettledDate = req.AmountSettledDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("estimate-line-items")]
    public async Task<ActionResult<IEnumerable<EstimateLineItemResponse>>> GetEstimateLineItems(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return Ok(new List<EstimateLineItemResponse>());

        return await _db.ClearanceEstimateLineItems
            .Where(x => x.ClearanceId == clearance.Id)
            .Include(x => x.ChargeType)
            .Select(x => new EstimateLineItemResponse(x.Id, x.ChargeTypeId, x.ChargeType!.Name, x.ValueSdg, x.DueDate))
            .ToListAsync();
    }

    [HttpPost("estimate-line-items")]
    public async Task<ActionResult<EstimateLineItemResponse>> AddEstimateLineItem(int shipmentId, EstimateLineItemRequest req)
    {
        var clearance = await GetOrCreateClearanceAsync(shipmentId);
        if (clearance is null) return NotFound();

        var entity = new ClearanceEstimateLineItem
        {
            ClearanceId = clearance.Id,
            ChargeTypeId = req.ChargeTypeId,
            ValueSdg = req.ValueSdg,
            DueDate = req.DueDate
        };
        _db.ClearanceEstimateLineItems.Add(entity);
        await _db.SaveChangesAsync();

        var chargeType = await _db.ClearanceChargeTypes.FindAsync(req.ChargeTypeId);
        return Ok(new EstimateLineItemResponse(entity.Id, entity.ChargeTypeId, chargeType?.Name ?? "", entity.ValueSdg, entity.DueDate));
    }

    [HttpDelete("estimate-line-items/{lineItemId:int}")]
    public async Task<IActionResult> DeleteEstimateLineItem(int shipmentId, int lineItemId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return NotFound();

        var entity = await _db.ClearanceEstimateLineItems.FirstOrDefaultAsync(x => x.Id == lineItemId && x.ClearanceId == clearance.Id);
        if (entity is null) return NotFound();

        _db.ClearanceEstimateLineItems.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
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
