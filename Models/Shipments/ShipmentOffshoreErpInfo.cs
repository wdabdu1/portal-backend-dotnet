using ShippingPortal.Api.Models.Orders;

namespace ShippingPortal.Api.Models.Shipments;

// One row per (Shipment, Offshore partner in that PO's chain). Which fields
// are actually meaningful depends on the partner's SequenceOrder in the
// chain — first hop (from Supplier) vs. every hop after — but all fields
// live on one row for simplicity; the unused half stays null.
public class ShipmentOffshoreErpInfo
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public int PurchaseOrderOffshorePartnerId { get; set; }
    public PurchaseOrderOffshorePartner? PurchaseOrderOffshorePartner { get; set; }

    // First offshore (SequenceOrder == 1) only
    public string? PrNo { get; set; }
    public string? PoNo { get; set; }
    public string? Sa { get; set; }
    public string? BillReg { get; set; }

    // Shared by first and subsequent offshores (label differs: "ERP GRN" vs "GRN No.")
    public string? Grn { get; set; }
    public string? InvoiceNo { get; set; }

    // Subsequent offshores (SequenceOrder > 1) only
    public string? InspectionNo { get; set; }
    public string? Remarks { get; set; }
}
