namespace ShippingPortal.Api.Models.Clearance;

public class ClearanceDeliveryOrder
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? CopyOfDoCollectedDate { get; set; }
    public DateOnly? ReceiveDoDate { get; set; }
    public DateOnly? ActualArrivalDate { get; set; }
    public bool DepositRequired { get; set; }
    public decimal? DoActualFeesSdg { get; set; }
    public DateOnly? DoFeesSettledDate { get; set; }
    public DateOnly? DoReceivedDate { get; set; } // completion marker
}

// EstimateValueSdg is intentionally NOT stored here — it's always the
// computed sum of this Clearance's ClearanceEstimateLineItems, returned by
// the API but never accepted as direct input.
public class ClearanceCostEstimate
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? EstimateDate { get; set; }
    public DateOnly? NotifyBuDate { get; set; }
    public DateOnly? AmountSettledDate { get; set; } // completion marker
}

// One row per charge in the cost estimate breakdown (DO Charges, DO
// Deposit, SPC, Customs Duties, etc. — open list, managed in Settings).
public class ClearanceEstimateLineItem
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public int ChargeTypeId { get; set; }
    public ClearanceChargeType? ChargeType { get; set; }
    public decimal ValueSdg { get; set; }
    public DateOnly? DueDate { get; set; }
}

public class ClearanceCertificateEntry
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? CertificateEntryDate { get; set; } // completion marker
    public string? ScudaDeclarationNo { get; set; }
}
