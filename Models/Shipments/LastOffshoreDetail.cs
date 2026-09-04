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
//
// C Pricing fields (added for the dedicated CPricing role's data-entry
// workflow, via api/c-pricing): CPricingCategoryId/CPricingTypeId classify
// the item, and CurrencyId is this item's OWN currency — Currency moved
// from shipment-level-only (see LastOffshoreDetail.CurrencyId above) to
// per-item, since items in the same shipment can genuinely be priced in
// different currencies. Older rows saved before this existed have a null
// CurrencyId here; callers should fall back to the shipment-level
// LastOffshoreDetail.CurrencyId in that case.
public class LastOffshoreItemDetail
{
    public int Id { get; set; }
    public int ShipmentLineItemId { get; set; }
    public ShipmentLineItem? ShipmentLineItem { get; set; }

    public string? Description { get; set; }
    public decimal? UnitPrice { get; set; }

    public int? CPricingCategoryId { get; set; }
    public CPricingCategory? CPricingCategory { get; set; }
    public int? CPricingTypeId { get; set; }
    public CPricingType? CPricingType { get; set; }
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    // Set (and re-set) every time this item is saved from the C Pricing
    // working table — "approval date" on the History page. Updates on every
    // re-save rather than only the first, since post-confirm edits stay
    // editable and the History page's default view is "most recently saved
    // first".
    public DateTime? CPricingSavedAt { get; set; }
}
