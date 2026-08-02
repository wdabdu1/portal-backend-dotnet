using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ErpColumnResponse(
    int PurchaseOrderOffshorePartnerId, string CompanyName, int SequenceOrder,
    string? PrNo, string? PoNo, string? Sa, string? BillReg, string? Grn, string? InvoiceNo,
    string? InspectionNo, string? Remarks);

public record ErpColumnRequest(
    string? PrNo, string? PoNo, string? Sa, string? BillReg, string? Grn, string? InvoiceNo,
    string? InspectionNo, string? Remarks);

[ApiController]
[Authorize]
[Route("api/shipments/{shipmentId:int}/erp-info")]
public class ShipmentOffshoreErpInfoController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShipmentOffshoreErpInfoController(ShippingPortalDbContext db) => _db = db;

    // One column per offshore partner in the shipment's PO chain, in order.
    // Existing saved data (if any) is merged in; partners with nothing saved
    // yet still appear with blank fields, so the UI always shows the full
    // chain even before anything's been entered.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ErpColumnResponse>>> GetColumns(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var offshorePartners = await _db.PurchaseOrderOffshorePartners
            .Where(op => op.PurchaseOrderId == shipment.PurchaseOrderId)
            .Include(op => op.BusinessPartner)
            .OrderBy(op => op.SequenceOrder)
            .ToListAsync();

        var existing = await _db.ShipmentOffshoreErpInfos
            .Where(e => e.ShipmentId == shipmentId)
            .ToDictionaryAsync(e => e.PurchaseOrderOffshorePartnerId);

        var result = offshorePartners.Select(op =>
        {
            existing.TryGetValue(op.Id, out var row);
            return new ErpColumnResponse(
                op.Id, op.BusinessPartner!.Name, op.SequenceOrder,
                row?.PrNo, row?.PoNo, row?.Sa, row?.BillReg, row?.Grn, row?.InvoiceNo,
                row?.InspectionNo, row?.Remarks);
        }).ToList();

        return Ok(result);
    }

    [HttpPut("{offshorePartnerId:int}")]
    public async Task<ActionResult<ErpColumnResponse>> SaveColumn(int shipmentId, int offshorePartnerId, ErpColumnRequest req)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var offshorePartner = await _db.PurchaseOrderOffshorePartners
            .Include(op => op.BusinessPartner)
            .FirstOrDefaultAsync(op => op.Id == offshorePartnerId && op.PurchaseOrderId == shipment.PurchaseOrderId);
        if (offshorePartner is null) return NotFound(new { message = "This offshore partner does not belong to this shipment's order." });

        var entity = await _db.ShipmentOffshoreErpInfos
            .FirstOrDefaultAsync(e => e.ShipmentId == shipmentId && e.PurchaseOrderOffshorePartnerId == offshorePartnerId);
        if (entity is null)
        {
            entity = new ShipmentOffshoreErpInfo { ShipmentId = shipmentId, PurchaseOrderOffshorePartnerId = offshorePartnerId };
            _db.ShipmentOffshoreErpInfos.Add(entity);
        }

        // Only the fields relevant to this partner's position get written —
        // e.g. saving a subsequent-offshore column never touches PrNo/PoNo/Sa/BillReg.
        if (offshorePartner.SequenceOrder == 1)
        {
            entity.PrNo = req.PrNo;
            entity.PoNo = req.PoNo;
            entity.Sa = req.Sa;
            entity.BillReg = req.BillReg;
            entity.Grn = req.Grn;
            entity.InvoiceNo = req.InvoiceNo;
        }
        else
        {
            entity.InspectionNo = req.InspectionNo;
            entity.Grn = req.Grn;
            entity.InvoiceNo = req.InvoiceNo;
            entity.Remarks = req.Remarks;
        }

        await _db.SaveChangesAsync();

        return Ok(new ErpColumnResponse(
            offshorePartnerId, offshorePartner.BusinessPartner!.Name, offshorePartner.SequenceOrder,
            entity.PrNo, entity.PoNo, entity.Sa, entity.BillReg, entity.Grn, entity.InvoiceNo,
            entity.InspectionNo, entity.Remarks));
    }
}
