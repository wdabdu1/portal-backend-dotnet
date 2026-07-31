using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Clearance;

public enum ClearanceRouteType
{
    NotSelected = 0,
    Route1ClearAtPort = 1,
    Route2FzDeposit = 2,
    Route3ClearFromFz = 3
}

// 1:1 with Shipment. General Info fields live directly here; route-specific
// detail tables (Route1/2/3) follow in a later round, same pattern as the
// Shipment sub-groups — this table is the entry point + route selector.
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

    public ClearanceRouteType Route { get; set; } = ClearanceRouteType.NotSelected;
    public DateOnly? ClearanceCompleteDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Dynamic, Settings-editable SLA targets. Not hardcoded to specific
// milestones — a flat list of (key, label, target days) rows, so more
// granular per-route milestones can be added later without a schema change.
public class ClearanceSlaSetting
{
    public int Id { get; set; }
    public string MilestoneKey { get; set; } = "";
    public string Label { get; set; } = "";
    public int TargetDays { get; set; }
    public bool IsActive { get; set; } = true;
}
