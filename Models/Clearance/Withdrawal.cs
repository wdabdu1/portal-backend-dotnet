using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Clearance;

// A withdrawal is its own independent workflow — NOT tied to a new
// Shipment/PO. It references the original deposit's Shipment directly
// (same BL/AWB No.), and one deposit can have any number of independent
// Withdrawal records over time, each with its own full processing.
public class Withdrawal
{
    public int Id { get; set; }
    public int DepositShipmentId { get; set; }
    public Shipment? DepositShipment { get; set; }

    // General Info
    public DateOnly? WithdrawalRequestDate { get; set; }
    public string? WithdrawalRequestRefNo { get; set; }

    // Customs Certificate Entry
    public DateOnly? CertificateEntryDate { get; set; }
    public string? ScudaDeclarationNo { get; set; }

    // SSMO (general approval, same concept as the Shipment page's SSMO
    // section) — distinct from the SSMO File Process / SSMO Examination
    // workflow steps further below.
    public bool? SsmoCocRequired { get; set; }
    public bool? SsmoCocAvailable { get; set; }
    public DateOnly? SsmoApplicationDate { get; set; }
    public decimal? SsmoCost { get; set; }
    public DateOnly? SsmoCostSettledDate { get; set; }
    public string? SsmoRefNumber { get; set; }
    public DateOnly? SsmoApprovalDate { get; set; }

    // MOT
    public DateOnly? MotApprovalDate { get; set; }

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
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class WithdrawalCostEstimate
{
    public int Id { get; set; }
    public int WithdrawalId { get; set; }
    public Withdrawal? Withdrawal { get; set; }

    public DateOnly? EstimateDate { get; set; }
    public DateOnly? NotifyBuDate { get; set; }
    public DateOnly? AmountSettledDate { get; set; }
}

public class WithdrawalEstimateLineItem
{
    public int Id { get; set; }
    public int WithdrawalId { get; set; }
    public Withdrawal? Withdrawal { get; set; }

    public int ChargeTypeId { get; set; }
    public ClearanceChargeType? ChargeType { get; set; }
    public decimal ValueSdg { get; set; }
    public DateOnly? DueDate { get; set; }
}

// Which deposited line items this withdrawal draws down, and how much —
// same role as ClearanceRoute3Withdrawal played before, now against the
// standalone Withdrawal entity instead.
public class WithdrawalLineItem
{
    public int Id { get; set; }
    public int WithdrawalId { get; set; }
    public Withdrawal? Withdrawal { get; set; }

    public int DepositShipmentLineItemId { get; set; }
    public ShipmentLineItem? DepositShipmentLineItem { get; set; }
    public decimal Qty { get; set; }
}
