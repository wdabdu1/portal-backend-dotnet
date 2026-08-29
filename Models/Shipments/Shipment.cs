using System.ComponentModel.DataAnnotations;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Orders;

namespace ShippingPortal.Api.Models.Shipments;

public enum ShipmentStatus
{
    Draft = 0,
    Confirmed = 1,
    Cancelled = 2
}

public class Shipment
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string BlAwbNo { get; set; } = "";
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public DateOnly? BlAwbDate { get; set; }
    public DateOnly? Etd { get; set; }
    public DateOnly? Eta { get; set; }
    public int ShippingLineId { get; set; }
    public ShippingLine? ShippingLine { get; set; }
    public string? VesselName { get; set; }
    public int Fcl20Count { get; set; }
    public int Fcl40Count { get; set; }
    public bool Soc { get; set; }
    public int? BlFreeDays { get; set; }
    public DateOnly? SobActualDate { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Draft;
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Direct Sales: dispatched straight under a client/consignee's name
    // rather than through the normal Clearance pipeline. Set once at
    // registration and never changed afterward — everything that keys
    // off it (Clearance gating, the Banking section variant, the Direct
    // Sales Finance page) assumes it's fixed for the life of the
    // shipment. ConsigneeName is captured here rather than reusing the
    // PO's own Consignee, since a Direct Sales order is sold on to an
    // end client that isn't necessarily the PO's registered Consignee.
    public bool IsDirectSales { get; set; }
    public string? ConsigneeName { get; set; }

    public ICollection<ShipmentLineItem> LineItems { get; set; } = new List<ShipmentLineItem>();

    // Every PO actually represented in this shipment's line items,
    // including the primary PurchaseOrderId above — for a normal
    // single-PO shipment this holds exactly one row, mirroring
    // PurchaseOrderId. Lets a shipment combine line items from multiple
    // POs (always same Supplier + Offshore chain + BU + Division, per
    // business rule) without changing what PurchaseOrderId means
    // anywhere else it's already used (display, offshore-chain lookup).
    // Dashboards that need to bucket a shipment under every PO it
    // touches (not just the primary one) should query this instead of
    // PurchaseOrderId.
    public ICollection<ShipmentPurchaseOrder> PurchaseOrderLinks { get; set; } = new List<ShipmentPurchaseOrder>();
}

public class ShipmentLineItem
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }
    public int PurchaseOrderLineItemId { get; set; }
    public PurchaseOrderLineItem? PurchaseOrderLineItem { get; set; }
    public decimal QtyInBl { get; set; }
    public decimal ItemSubtotal { get; set; }
    public string? HsCode { get; set; }
}

// Join row: one per PO contributing line items to a shipment (see
// Shipment.PurchaseOrderLinks above).
public class ShipmentPurchaseOrder
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
}
