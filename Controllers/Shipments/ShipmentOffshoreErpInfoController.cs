using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ErpColumnResponse(
    int PurchaseOrderOffshorePartnerId, string CompanyName, int SequenceOrder,
    string? PrNo, string? PoNo, string? Sa, string? BillReg, string? Grn, string? InvoiceNo,
    string? InspectionNo, string? Remarks, bool IsLast);

public record ErpColumnRequest(
    string? PrNo, string? PoNo, string? Sa, string? BillReg, string? Grn, string? InvoiceNo,
    string? InspectionNo, string? Remarks);

[ApiController]
[Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
[Route("api/shipments/{shipmentId:int}/erp-info")]
public class ShipmentOffshoreErpInfoController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.BuAccessService _buAccess;

    public ShipmentOffshoreErpInfoController(ShippingPortalDbContext db, ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

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

        var maxSequence = offshorePartners.Count > 0 ? offshorePartners.Max(op => op.SequenceOrder) : 0;

        var result = offshorePartners.Select(op =>
        {
            existing.TryGetValue(op.Id, out var row);
            return new ErpColumnResponse(
                op.Id, op.BusinessPartner!.Name, op.SequenceOrder,
                row?.PrNo, row?.PoNo, row?.Sa, row?.BillReg, row?.Grn, row?.InvoiceNo,
                row?.InspectionNo, row?.Remarks, op.SequenceOrder == maxSequence);
        }).ToList();

        return Ok(result);
    }

    [HttpPut("{offshorePartnerId:int}")]
    public async Task<ActionResult<ErpColumnResponse>> SaveColumn(int shipmentId, int offshorePartnerId, ErpColumnRequest req)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();
        if (!_buAccess.CanWriteBusinessUnit(User, shipment.PurchaseOrderId is var poId && poId != 0
                ? await _db.PurchaseOrders.Where(p => p.Id == shipment.PurchaseOrderId).Select(p => p.BusinessUnitId).FirstOrDefaultAsync()
                : 0))
            return Forbid();

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

        var maxSeq = await _db.PurchaseOrderOffshorePartners
            .Where(op => op.PurchaseOrderId == shipment.PurchaseOrderId)
            .MaxAsync(op => (int?)op.SequenceOrder) ?? 0;

        return Ok(new ErpColumnResponse(
            offshorePartnerId, offshorePartner.BusinessPartner!.Name, offshorePartner.SequenceOrder,
            entity.PrNo, entity.PoNo, entity.Sa, entity.BillReg, entity.Grn, entity.InvoiceNo,
            entity.InspectionNo, entity.Remarks, offshorePartner.SequenceOrder == maxSeq));
    }
}
