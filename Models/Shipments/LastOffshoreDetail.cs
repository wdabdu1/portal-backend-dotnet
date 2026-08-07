using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Shipments;

// 1:1 with Shipment. PI No. is deliberately not stored here — it's read
// live from MOT's Offshore Approved PI Number, since that's the true
// source of that value.
public class LastOffshoreDetail
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public string? InspectionNo { get; set; }
    public string? Grn { get; set; }
    public string? InvoiceNo { get; set; }
    public string? Remarks { get; set; }

    // One currency for every item in this shipment's Last Offshore Details.
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
}

// Extends an existing ShipmentLineItem with the extra fields Last Offshore
// Details needs (Description, Unit Price). HS Code stays on
// ShipmentLineItem itself — this just adds what ShipmentLineItem doesn't
// already have, rather than duplicating it.
public class LastOffshoreItemDetail
{
    public int Id { get; set; }
    public int ShipmentLineItemId { get; set; }
    public ShipmentLineItem? ShipmentLineItem { get; set; }

    public string? Description { get; set; }
    public decimal? UnitPrice { get; set; }
}
