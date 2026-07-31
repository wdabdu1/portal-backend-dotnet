namespace ShippingPortal.Api.Models.Clearance;

public class ClearanceDeliveryOrder
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? CopyOfDoCollectedDate { get; set; }
    public DateOnly? ReceiveDoDate { get; set; }
    public DateOnly? ActualArrivalDate { get; set; }
    public decimal? DoFeesSdg { get; set; }
    public DateOnly? DoFeesSettledDate { get; set; }
    public DateOnly? DoReceivedDate { get; set; } // completion marker
}

public class ClearanceCostEstimate
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? EstimateDate { get; set; }
    public decimal? EstimateValueSdg { get; set; }
    public DateOnly? NotifyBuDate { get; set; }
    public DateOnly? AmountSettledDate { get; set; } // completion marker
}

public class ClearanceCertificateEntry
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public DateOnly? CertificateEntryDate { get; set; } // completion marker
    public string? ScudaDeclarationNo { get; set; }
}
