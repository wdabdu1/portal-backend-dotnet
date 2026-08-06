using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Clearance;

public enum ClearanceRouteType
{
    NotSelected = 0,
    Route1ClearAtPort = 1,
    Route2FzDeposit = 2,
    Route3ClearFromFz = 3
}

public class Clearance
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }

    public DateOnly? CopyOfBlReceivedDate { get; set; }
    public DateOnly? OriginalShipmentSetReceivedDate { get; set; }
    public string? LcNo { get; set; }
    public string? DeclarationNo { get; set; }
    public string? Notes { get; set; }
    public string? ImFormNo { get; set; }
    public DateOnly? ImFormDate { get; set; }

    // Route 3 only — replaces Copy of BL/ETA as the SLA anchor for
    // withdrawals, since there's no vessel arrival involved.
    public DateOnly? WithdrawalRequestDate { get; set; }
    public string? WithdrawalRequestRefNo { get; set; }

    public ClearanceRouteType Route { get; set; } = ClearanceRouteType.NotSelected;
    public DateOnly? ClearanceCompleteDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Fixed division keys — match the "Division within Page" column from the
// Clearance field spec.
public static class ClearanceDivision
{
    public const string General = "ClearanceGeneral";
    public const string Route1 = "Route1";
    public const string Route2 = "Route2";
    public const string Route3 = "Route3";
}

// One row per Group Item (not per individual field) — e.g. "SSMO File
// Process" within Route-1. TargetDays is the only user-editable value;
// Division/GroupItem/SequenceOrder are fixed by the seeder. A route's total
// duration is the sum of its Group Items' TargetDays, computed on read.
public class ClearanceSlaSetting
{
    public int Id { get; set; }
    public string Division { get; set; } = "";
    public string GroupItem { get; set; } = "";
    public int SequenceOrder { get; set; }
    public decimal TargetDays { get; set; }
    public bool IsActive { get; set; } = true;
}
