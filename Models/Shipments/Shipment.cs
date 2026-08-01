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
    public int Fcl20Count { get; set; }
    public int Fcl40Count { get; set; }
    public bool Soc { get; set; }
    public int? BlFreeDays { get; set; }
    public DateOnly? SobActualDate { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Draft;
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ShipmentLineItem> LineItems { get; set; } = new List<ShipmentLineItem>();
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
}
