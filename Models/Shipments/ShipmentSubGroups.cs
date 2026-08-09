using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Shipments;

// Each of these is a 1:1 child of Shipment, created/updated independently
// as its own "Group Items" section in the Update Order accordion. Nullable
// FKs on cost/currency fields since a section may be partially filled in
// before being completed.

public class ShipmentForwarder
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public int? ForwarderId { get; set; }
    public Forwarder? ForwarderEntity { get; set; }
    public decimal? ActualShippingCost { get; set; }
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal? ActualShippingCostUsd { get; set; }
    public decimal? AmountSaved { get; set; }
    public bool MarineInsurance { get; set; }
}

public class ShipmentAcd
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly? ProcessDate { get; set; }
    public decimal? CostUsd { get; set; }
    public DateOnly? CostSettledDate { get; set; }
    public string? RefNumber { get; set; }
}

public class ShipmentDraftDocuments
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly? InitialDraftReceivedDate { get; set; }
    public DateOnly? FinalDraftReceivedDate { get; set; }
    public DateOnly? FinalDraftConfirmedDate { get; set; }
}

public class ShipmentSsmo
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly? ApplicationDate { get; set; }
    public decimal? Cost { get; set; }
    public DateOnly? CostSettledDate { get; set; }
    public string? RefNumber { get; set; }
    public DateOnly? ApprovalDate { get; set; }
}

public class ShipmentMot
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly? ProcessDate { get; set; }
    public decimal? Cost { get; set; }
    public DateOnly? CostSettledDate { get; set; }
    public string? RefNumber { get; set; }
    public DateOnly? ApprovalDate { get; set; }
    public string? OffshoreApprovedPiNumber { get; set; }
}

public class ShipmentSupplierFullSet
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public string? SupplierInvoiceNo { get; set; }
    public DateOnly? SupplierInvoiceDate { get; set; }
    public DateOnly? FsDispatchDate { get; set; }
    public int? FsDispatchedViaId { get; set; }
    public Courier? FsDispatchedVia { get; set; }
    public string? FsTrackingNumber { get; set; }
    public DateOnly? FsReceivedDate { get; set; }
}

// Invoice Value/Currency are fully computed (sum of ShipmentLineItem
// subtotals) — nothing stored here for the invoice header anymore.
// Only the actual payment records are real data now.
// Freely-entered — no formula/anchor-date logic. Real supplier terms
// (e.g. 30% advance / 70% on shipment) vary too much per shipment to
// compute automatically, so the user enters exactly what was agreed.
public class ShipmentPaymentDue
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public string? Label { get; set; } // e.g. "30% Advance", "Balance on BL"
}

// Invoice Value/Currency are fully computed (sum of ShipmentLineItem
// subtotals) — nothing stored here for the invoice header anymore.
// Only the actual payment records are real data now.
public class ShipmentSupplierPaymentRecord
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly PaymentDate { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal Value { get; set; }
    public decimal ValueUsd { get; set; }

    // Optional — links this actual payment to a specific due-schedule
    // row, so Supplier Dues can show Due vs. Paid vs. Remaining per
    // milestone. Left null, a payment is just an unlinked lump payment,
    // same as it works today.
    public int? PaymentDueId { get; set; }
    public ShipmentPaymentDue? PaymentDue { get; set; }
}
public class ShipmentBanking
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public int? SenderBankId { get; set; }
    public SenderBank? SenderBank { get; set; }
    public DateOnly? OsDocDispatchDate { get; set; }
    public int? OsDocDispatchedViaId { get; set; }
    public Courier? OsDocDispatchedVia { get; set; }
    public string? OsDocTrackingNumber { get; set; }
    public decimal? SenderBankCharges { get; set; }

    public int? ReceivingBankId { get; set; }
    public ReceiverBank? ReceivingBank { get; set; }
    public bool NecessaryGoodType { get; set; }
    public string? CollectionRefNo { get; set; }
    public decimal? CollectionValue { get; set; }
    public int? CollectionCurrencyId { get; set; }
    public Currency? CollectionCurrency { get; set; }
    public int? TenorId { get; set; }
    public Tenor? Tenor { get; set; }
    public decimal? ReceiverBankCharges { get; set; }
}
public class ShipmentCollectionRecord
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly PaymentDate { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal Value { get; set; }
    public decimal ValueUsd { get; set; }
}
