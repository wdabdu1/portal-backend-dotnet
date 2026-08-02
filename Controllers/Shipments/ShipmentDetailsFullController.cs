using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ShipmentLineItemDetail(string ProductCategory, string ModelProduct, decimal QtyInBl, string? UnitOfMeasure);

public record ErpColumnDetail(string CompanyName, int SequenceOrder, bool IsLast, object? Data);

public record ShipmentFullDetailResponse(
    int Id, string BlAwbNo, string PoNumber, string Status,
    string BusinessUnit, string? Supplier, string Consignee, string Category,
    int Fcl20Count, int Fcl40Count, DateOnly? Eta, DateOnly? SobActualDate,
    List<ShipmentLineItemDetail> LineItems,
    object? Forwarder, object? Acd, object? DraftDocuments, object? Ssmo, object? Mot,
    object? SupplierFullSet, object? Banking,
    List<ErpColumnDetail> ErpInfo);

[ApiController]
[Authorize]
[Route("api/shipments/{id:int}/full-details")]
public class ShipmentDetailsFullController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShipmentDetailsFullController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ShipmentFullDetailResponse>> Get(int id, [FromServices] BuAccessService buAccess)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (shipment is null) return NotFound();
        if (!buAccess.SeesAllBus(User) && !buAccess.CanSeeBusinessUnit(User, shipment.PurchaseOrder!.BusinessUnitId)) return Forbid();

        var isClearance = User.IsInRole(AppRoles.Clearance);

        var forwarder = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == id);
        var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == id);
        var draftDocs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == id);
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == id);
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == id);
        var supplierFullSet = isClearance ? null : (object?)await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == id);
