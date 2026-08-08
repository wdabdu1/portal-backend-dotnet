using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models;

// One row per (Line Item, Offshore stage) — the profitability ledger.
// MarkupPercent is user-entered for every stage except the last one
// (where it's calculated backward from the real invoice total captured
// in Last Offshore Details). Total/TotalUsd are always server-computed,
// never trusted from the client.
public class TransferPricingEntry
{
    public int Id { get; set; }

    public int ShipmentLineItemId { get; set; }
    public ShipmentLineItem? ShipmentLineItem { get; set; }

    public int PurchaseOrderOffshorePartnerId { get; set; }
    public PurchaseOrderOffshorePartner? PurchaseOrderOffshorePartner { get; set; }

    public decimal? MarkupPercent { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal Total { get; set; }
    public decimal TotalUsd { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
