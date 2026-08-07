using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Logistics;

// Stage 1 — the Logistics Officer's decision on how much of a cleared
// item goes to which warehouse. Sourced from exactly one of two places:
// a Route 1 (port) shipment's own line item, or a specific FZ withdrawal's
// line item — never both.
public class WarehouseAllocation
{
    public int Id { get; set; }

    public int? ShipmentLineItemId { get; set; }
    public ShipmentLineItem? ShipmentLineItem { get; set; }

    public int? WithdrawalLineItemId { get; set; }
    public WithdrawalLineItem? WithdrawalLineItem { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public decimal Qty { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
}
