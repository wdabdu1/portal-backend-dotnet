using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record LookupRef(int Id, string Name);

public record PoCorrectionResponse(
    int Id, string PoNumber, string Status, string BusinessUnit,
    LookupRef OriginCountry, LookupRef Incoterm, LookupRef ShipmentMode, LookupRef PaymentTerm, LookupRef ApprovalType, LookupRef BrandManufacturer,
    LookupRef? PortOfLoading, LookupRef? PortOfDischarge,
    string? OffshorePoNo, DateOnly? OffshorePoDate, string? SupplierPiNo, DateOnly? SupplierPiDate,
    DateOnly? ReceivedSignedPiDate, DateOnly? SentSignedPiDate, DateOnly? BuPoDate, DateOnly? OrderExecutionDate, DateOnly? LatestShippingDate);

public record PoCorrectionRequest(
    int OriginCountryId, int IncotermId, int ShipmentModeId, int SupplierPaymentTermId, int ApprovalTypeId, int BrandManufacturerId,
    int? PortOfLoadingId, int? PortOfDischargeId,
    string? OffshorePoNo, DateOnly? OffshorePoDate, string? SupplierPiNo, DateOnly? SupplierPiDate,
    DateOnly? ReceivedSignedPiDate, DateOnly? SentSignedPiDate, DateOnly? BuPoDate, DateOnly? OrderExecutionDate, DateOnly? LatestShippingDate);

public record ShipmentCorrectionLine(int LineItemId, string ModelProduct, string? HsCode);
public record ShipmentCorrectionLineRequest(int LineItemId, string? HsCode);

public record ShipmentCorrectionResponse(
    int Id, string BlAwbNo, string PoNumber, string Status, string BusinessUnit,
    DateOnly? BlAwbDate, DateOnly? Etd, DateOnly? Eta, LookupRef ShippingLine, string? VesselName,
    int Fcl20Count, int Fcl40Count, bool Soc, int? BlFreeDays, List<ShipmentCorrectionLine> LineItems);

public record ShipmentCorrectionRequest(
    DateOnly? BlAwbDate, DateOnly? Etd, DateOnly? Eta, int ShippingLineId, string? VesselName,
    int Fcl20Count, int Fcl40Count, bool Soc, int? BlFreeDays, List<ShipmentCorrectionLineRequest>? LineItems);

public record ChangeLogRow(int Id, string FieldName, string? OldValue, string? NewValue, string ChangedByName, DateTime ChangedAt);

// Phase 1 of "correct an existing PO / Shipment": only DESCRIPTIVE fields —
// nothing that feeds a stored total, payment due, advance, transfer price or an
// import key. (Deliberately NOT editable here: Supplier, BU, Division, Consignee,
// PO number, BL/AWB number, PO status, line quantities / prices / currency, the
// offshore chain, shipment line quantities and values, the PO link, Ship-on-Board
// date [already has its own section on Update Shipment].)
//
// Manager / SuperUser only. Every changed field is written to DataChangeLogs
// (who / when / old -> new) in the same transaction as the change itself.
[ApiController]
[Authorize(Roles = AppRoles.RecordCorrectors)]
[Route("api/corrections")]
public class RecordCorrectionsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public RecordCorrectionsController(ShippingPortalDbContext db) => _db = db;

    // ---------- helpers ----------
    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Fmt(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static LookupRef? Ref(int? id, string? name) => id.HasValue && name is not null ? new LookupRef(id.Value, name) : null;

    private async Task<string> CurrentUserNameAsync(string userId)
    {
        var name = await _db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(name) ? (User.Identity?.Name ?? "") : name;
    }

    // Collects one DataChangeLog per field that actually changed, and applies
    // the new value to the entity. Each method returns false when the new
    // value is invalid (unknown lookup id) so the caller can reject the whole
    // request before anything is saved.
    private sealed class Changes
    {
        public readonly List<DataChangeLog> Rows = new();
        private readonly string _type, _ref, _userId, _userName;
        private readonly int _entityId;
        private readonly DateTime _at = DateTime.UtcNow;
        public string? Error;

        public Changes(string type, int entityId, string entityRef, string userId, string userName)
        { _type = type; _entityId = entityId; _ref = entityRef; _userId = userId; _userName = userName; }

        private void Add(string field, string? oldV, string? newV)
        {
            if (field.Length > 80) field = field[..80];
            Rows.Add(new DataChangeLog
            {
                EntityType = _type, EntityId = _entityId, EntityRef = _ref, FieldName = field,
                OldValue = oldV is { Length: > 500 } ? oldV[..500] : oldV,
                NewValue = newV is { Length: > 500 } ? newV[..500] : newV,
                ChangedByUserId = _userId, ChangedByName = _userName, ChangedAt = _at
            });
        }

        public void Text(string field, string? oldV, string? newV, Action<string?> apply)
        {
            var o = Norm(oldV); var n = Norm(newV);
            if (o == n) return;
            Add(field, o, n); apply(n);
        }

        public void Date(string field, DateOnly? oldV, DateOnly? newV, Action<DateOnly?> apply)
        {
            if (oldV == newV) return;
            Add(field, Fmt(oldV), Fmt(newV)); apply(newV);
        }

        public void Number(string field, int? oldV, int? newV, Action<int?> apply)
        {
            if (oldV == newV) return;
            Add(field, oldV?.ToString(CultureInfo.InvariantCulture), newV?.ToString(CultureInfo.InvariantCulture)); apply(newV);
        }

        public void Flag(string field, bool oldV, bool newV, Action<bool> apply)
        {
            if (oldV == newV) return;
            Add(field, oldV ? "Yes" : "No", newV ? "Yes" : "No"); apply(newV);
        }

        public async Task Lookup(string field, int? oldId, string? oldName, int? newId, Func<int, Task<string?>> nameOf, Action<int?> apply)
        {
            if (Error is not null || newId == oldId) return;
            string? newName = null;
            if (newId is not null)
            {
                newName = await nameOf(newId.Value);
                if (newName is null) { Error = $"Invalid selection for {field}."; return; }
            }
            Add(field, oldName, newName); apply(newId);
        }
    }

    // ---------- Purchase Orders ----------
    [HttpGet("orders/{id:int}")]
    public async Task<ActionResult<PoCorrectionResponse>> GetOrder(int id)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.BusinessUnit).Include(p => p.OriginCountry).Include(p => p.Incoterm).Include(p => p.ShipmentMode)
            .Include(p => p.SupplierPaymentTerm).Include(p => p.ApprovalType).Include(p => p.BrandManufacturer)
            .Include(p => p.PortOfLoading).Include(p => p.PortOfDischarge)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        return new PoCorrectionResponse(
            po.Id, po.PoNumber, po.Status.ToString(), po.BusinessUnit?.Name ?? "",
            new LookupRef(po.OriginCountryId, po.OriginCountry?.Name ?? ""),
            new LookupRef(po.IncotermId, po.Incoterm?.Name ?? ""),
            new LookupRef(po.ShipmentModeId, po.ShipmentMode?.Name ?? ""),
            new LookupRef(po.SupplierPaymentTermId, po.SupplierPaymentTerm?.Name ?? ""),
            new LookupRef(po.ApprovalTypeId, po.ApprovalType?.Name ?? ""),
            new LookupRef(po.BrandManufacturerId, po.BrandManufacturer?.Name ?? ""),
            Ref(po.PortOfLoadingId, po.PortOfLoading?.Name), Ref(po.PortOfDischargeId, po.PortOfDischarge?.Name),
            po.OffshorePoNo, po.OffshorePoDate, po.SupplierPiNo, po.SupplierPiDate,
            po.ReceivedSignedPiDate, po.SentSignedPiDate, po.BuPoDate, po.OrderExecutionDate, po.LatestShippingDate);
    }

    [HttpPut("orders/{id:int}")]
    public async Task<IActionResult> SaveOrder(int id, PoCorrectionRequest req)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.OriginCountry).Include(p => p.Incoterm).Include(p => p.ShipmentMode)
            .Include(p => p.SupplierPaymentTerm).Include(p => p.ApprovalType).Include(p => p.BrandManufacturer)
            .Include(p => p.PortOfLoading).Include(p => p.PortOfDischarge)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (po is null) return NotFound();

        if ((req.OffshorePoNo?.Trim().Length ?? 0) > 60 || (req.SupplierPiNo?.Trim().Length ?? 0) > 60)
            return BadRequest(new { message = "Offshore PO No. and Supplier PI No. can be at most 60 characters." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var c = new Changes("PurchaseOrder", po.Id, po.PoNumber, userId, await CurrentUserNameAsync(userId));

        await c.Lookup("Origin Country", po.OriginCountryId, po.OriginCountry?.Name, req.OriginCountryId,
            i => _db.OriginCountries.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.OriginCountryId = v!.Value);
        await c.Lookup("Incoterm", po.IncotermId, po.Incoterm?.Name, req.IncotermId,
            i => _db.Incoterms.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.IncotermId = v!.Value);
        await c.Lookup("Shipment Mode", po.ShipmentModeId, po.ShipmentMode?.Name, req.ShipmentModeId,
            i => _db.ShipmentModes.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.ShipmentModeId = v!.Value);
        await c.Lookup("Payment Term", po.SupplierPaymentTermId, po.SupplierPaymentTerm?.Name, req.SupplierPaymentTermId,
            i => _db.PaymentTerms.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.SupplierPaymentTermId = v!.Value);
        await c.Lookup("Approval Type", po.ApprovalTypeId, po.ApprovalType?.Name, req.ApprovalTypeId,
            i => _db.ApprovalTypes.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.ApprovalTypeId = v!.Value);
        await c.Lookup("Brand/Manufacturer", po.BrandManufacturerId, po.BrandManufacturer?.Name, req.BrandManufacturerId,
            i => _db.BusinessPartners.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.BrandManufacturerId = v!.Value);
        await c.Lookup("Port of Loading", po.PortOfLoadingId, po.PortOfLoading?.Name, req.PortOfLoadingId,
            i => _db.Ports.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.PortOfLoadingId = v);
        await c.Lookup("Port of Discharge", po.PortOfDischargeId, po.PortOfDischarge?.Name, req.PortOfDischargeId,
            i => _db.Ports.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => po.PortOfDischargeId = v);
        if (c.Error is not null) return BadRequest(new { message = c.Error });

        c.Text("Offshore PO No.", po.OffshorePoNo, req.OffshorePoNo, v => po.OffshorePoNo = v);
        c.Date("Offshore PO Date", po.OffshorePoDate, req.OffshorePoDate, v => po.OffshorePoDate = v);
        c.Text("Supplier PI No.", po.SupplierPiNo, req.SupplierPiNo, v => po.SupplierPiNo = v);
        c.Date("Supplier PI Date", po.SupplierPiDate, req.SupplierPiDate, v => po.SupplierPiDate = v);
        c.Date("Received Signed PI Date", po.ReceivedSignedPiDate, req.ReceivedSignedPiDate, v => po.ReceivedSignedPiDate = v);
        c.Date("Sent Signed PI Date", po.SentSignedPiDate, req.SentSignedPiDate, v => po.SentSignedPiDate = v);
        c.Date("BU PO Date", po.BuPoDate, req.BuPoDate, v => po.BuPoDate = v);
        c.Date("Order Execution Date", po.OrderExecutionDate, req.OrderExecutionDate, v => po.OrderExecutionDate = v);
        c.Date("Latest Shipping Date", po.LatestShippingDate, req.LatestShippingDate, v => po.LatestShippingDate = v);

        if (c.Rows.Count == 0) return Ok(new { changed = 0 });

        po.UpdatedAt = DateTime.UtcNow;
        _db.DataChangeLogs.AddRange(c.Rows);
        await _db.SaveChangesAsync();
        return Ok(new { changed = c.Rows.Count });
    }

    // ---------- Shipments ----------
    [HttpGet("shipments/{id:int}")]
    public async Task<ActionResult<ShipmentCorrectionResponse>> GetShipment(int id)
    {
        var s = await _db.Shipments
            .Include(x => x.PurchaseOrder!).ThenInclude(p => p.BusinessUnit)
            .Include(x => x.ShippingLine)
            .Include(x => x.LineItems).ThenInclude(li => li.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        return new ShipmentCorrectionResponse(
            s.Id, s.BlAwbNo, s.PurchaseOrder?.PoNumber ?? "", s.Status.ToString(), s.PurchaseOrder?.BusinessUnit?.Name ?? "",
            s.BlAwbDate, s.Etd, s.Eta, new LookupRef(s.ShippingLineId, s.ShippingLine?.Name ?? ""), s.VesselName,
            s.Fcl20Count, s.Fcl40Count, s.Soc, s.BlFreeDays,
            s.LineItems.OrderBy(li => li.Id)
                .Select(li => new ShipmentCorrectionLine(li.Id, li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "", li.HsCode)).ToList());
    }

    [HttpPut("shipments/{id:int}")]
    public async Task<IActionResult> SaveShipment(int id, ShipmentCorrectionRequest req)
    {
        var s = await _db.Shipments
            .Include(x => x.ShippingLine)
            .Include(x => x.LineItems).ThenInclude(li => li.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        if (req.Fcl20Count < 0 || req.Fcl40Count < 0) return BadRequest(new { message = "Container counts cannot be negative." });
        if (req.BlFreeDays is < 0) return BadRequest(new { message = "BL free days cannot be negative." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var c = new Changes("Shipment", s.Id, s.BlAwbNo, userId, await CurrentUserNameAsync(userId));

        await c.Lookup("Shipping Line", s.ShippingLineId, s.ShippingLine?.Name, req.ShippingLineId,
            i => _db.ShippingLines.Where(x => x.Id == i).Select(x => x.Name).FirstOrDefaultAsync(), v => s.ShippingLineId = v!.Value);
        if (c.Error is not null) return BadRequest(new { message = c.Error });

        c.Date("BL/AWB Date", s.BlAwbDate, req.BlAwbDate, v => s.BlAwbDate = v);
        c.Date("ETD", s.Etd, req.Etd, v => s.Etd = v);
        c.Date("ETA", s.Eta, req.Eta, v => s.Eta = v);
        c.Text("Vessel Name", s.VesselName, req.VesselName, v => s.VesselName = v);
        c.Number("FCL 20' Count", s.Fcl20Count, req.Fcl20Count, v => s.Fcl20Count = v!.Value);
        c.Number("FCL 40' Count", s.Fcl40Count, req.Fcl40Count, v => s.Fcl40Count = v!.Value);
        c.Flag("SOC", s.Soc, req.Soc, v => s.Soc = v);
        c.Number("BL Free Days", s.BlFreeDays, req.BlFreeDays, v => s.BlFreeDays = v);

        foreach (var line in req.LineItems ?? new List<ShipmentCorrectionLineRequest>())
        {
            var li = s.LineItems.FirstOrDefault(x => x.Id == line.LineItemId);
            if (li is null) continue;   // ignore ids that don't belong to this shipment
            var model = li.PurchaseOrderLineItem?.ModelProduct?.Name ?? $"line {li.Id}";
            c.Text($"HS Code ({(model.Length > 55 ? model[..55] : model)})", li.HsCode, line.HsCode, v => li.HsCode = v);
        }

        if (c.Rows.Count == 0) return Ok(new { changed = 0 });

        s.UpdatedAt = DateTime.UtcNow;
        _db.DataChangeLogs.AddRange(c.Rows);
        await _db.SaveChangesAsync();
        return Ok(new { changed = c.Rows.Count });
    }

    // ---------- History ----------
    // entityType: "orders" | "shipments"
    [HttpGet("history/{entityType}/{id:int}")]
    public async Task<ActionResult<IEnumerable<ChangeLogRow>>> GetHistory(string entityType, int id)
    {
        var type = entityType switch { "orders" => "PurchaseOrder", "shipments" => "Shipment", _ => null };
        if (type is null) return NotFound();

        var rows = await _db.DataChangeLogs
            .Where(x => x.EntityType == type && x.EntityId == id)
            .OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id)
            .Select(x => new ChangeLogRow(x.Id, x.FieldName, x.OldValue, x.NewValue, x.ChangedByName, x.ChangedAt))
            .ToListAsync();
        return Ok(rows);
    }
}
