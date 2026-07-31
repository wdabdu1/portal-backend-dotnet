namespace ShippingPortal.Api.Models.Clearance;

// Each of these is 1:1 with Clearance, but only ever populated for the
// route actually selected on that shipment's Clearance. Group Items match
// the spec exactly: Containers Move Process, SSMO File Process, Customs
// Examination (Form 48), Customs Lab, SSMO Examination, Customs Evaluation,
// SPC Bill, Truck & Containers.
public class ClearanceRoute1Details
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    // Containers Move Process
    public DateOnly? MoveRequestDate { get; set; }
    public decimal? BillAmountSdg { get; set; }
    public DateOnly? BillSettlementDate { get; set; }

    // SSMO File Process
    public DateOnly? SsmoFileRequestDate { get; set; }
    public decimal? SsmoInspectionAmountSdg { get; set; }
    public DateOnly? SsmoFeesSettlementDate { get; set; }

    // Customs Examination (Form 48)
    public DateOnly? CustExamStartDate { get; set; }
    public DateOnly? CustExamCompletedDate { get; set; }

    // Customs Lab
    public bool CustomsLabRequired { get; set; }
    public decimal? CustomsLabFeesSdg { get; set; }
    public DateOnly? LabFeesPaymentDate { get; set; }
    public DateOnly? LabResultIssuanceDate { get; set; }

    // SSMO Examination
    public DateOnly? SsmoExamStartDate { get; set; }
    public DateOnly? SsmoCertIssuanceDate { get; set; }

    // Customs Evaluation
    public DateOnly? CustEvaluationDate { get; set; }
    public decimal? CustomsDutySdg { get; set; }
    public DateOnly? CustomsSettlementDate { get; set; }
    public DateOnly? ReleaseExitPassDate { get; set; }

    // SPC Bill
    public DateOnly? SpcBillRequestDate { get; set; }
    public decimal? SpcBillValueSdg { get; set; }
    public DateOnly? SpcBillSettlementDate { get; set; }

    // Truck & Containers
    public DateOnly? TruckPortEntryPermitDate { get; set; }
    public DateOnly? ContainersReturnedDate { get; set; }
    public DateOnly? ClearanceActualCompletedDate { get; set; }
}

public class ClearanceRoute2Details
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    // FZ Deposit Request
    public DateOnly? DepositRequestDate { get; set; }
    public DateOnly? RequestApprovalDate { get; set; }

    // Customs Inspection
    public DateOnly? InspectionDate { get; set; }

    // SPC Bill
    public DateOnly? SpcBillRequestDate { get; set; }
    public decimal? SpcBillValueSdg { get; set; }
    public DateOnly? SpcBillSettlementDate { get; set; }
    public DateOnly? PoliceSecurityAppointedDate { get; set; }

    // Truck & Containers
    public DateOnly? TruckPortEntryPermitDate { get; set; }
    public DateOnly? ContainersReceivedAtFzDate { get; set; }
    public DateOnly? ContainersReturnedDate { get; set; }
    public DateOnly? ClearanceActualCompletedDate { get; set; }
}

public class ClearanceRoute3Details
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    // Customs Certificate Entry
    public DateOnly? CertificateEntryDate { get; set; }
    public string? ScudaDeclarationNo { get; set; }

    // SSMO File Process
    public DateOnly? SsmoFileRequestDate { get; set; }
    public decimal? SsmoInspectionAmountSdg { get; set; }
    public DateOnly? SsmoFeesSettlementDate { get; set; }

    // Customs Examination (Form 48)
    public DateOnly? CustExamStartDate { get; set; }
    public DateOnly? CustExamCompletedDate { get; set; }

    // Customs Lab
    public bool CustomsLabRequired { get; set; }
    public decimal? CustomsLabFeesSdg { get; set; }
    public DateOnly? LabFeesPaymentDate { get; set; }
    public DateOnly? LabResultIssuanceDate { get; set; }

    // SSMO Examination
    public DateOnly? SsmoExamStartDate { get; set; }
    public DateOnly? SsmoCertIssuanceDate { get; set; }

    // Customs Evaluation
    public DateOnly? CustEvaluationDate { get; set; }
    public decimal? CustomsDutySdg { get; set; }
    public DateOnly? CustomsSettlementDate { get; set; }
    public DateOnly? ReleaseExitPassDate { get; set; }

    // Truck & Containers
    public DateOnly? TruckPortEntryPermitDate { get; set; }
    public DateOnly? ClearanceActualCompletedDate { get; set; }
}
