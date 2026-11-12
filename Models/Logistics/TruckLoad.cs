using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Logistics;

// One truck, one trip, one date — can carry items for multiple drops
// (multi-drop). Driver is snapshotted here (not just read live off Truck)
// so history stays accurate even if the truck's assigned driver changes later.
// Internal (own fleet) or External (third-party transporter) — see IsExternal.
public class TruckLoad
{
    public int Id { get; set; }

    // Internal loads: TruckId (and usually DriverId) point at the in-house fleet.
    // External loads: TruckId stays null and the ExternalXxx fields below carry
    // the transporter + the truck/driver details typed in for THIS trip.
    public int? TruckId { get; set; }
    public Truck? Truck { get; set; }
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    // The decision taken when the truck was assigned. Existing rows are Internal
    // (the column defaults to false).
    public bool IsExternal { get; set; }
    public int? ExternalTransporterId { get; set; }
    public ExternalTransporter? ExternalTransporter { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(40)] public string? ExternalTruckNo { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(120)] public string? ExternalDriverName { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(60)] public string? ExternalDriverPhone { get; set; }

    public DateOnly LoadDate { get; set; }
    public string? Notes { get; set; }

    // One place to read "which truck / which driver" regardless of mode.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string TruckDisplay => IsExternal ? (ExternalTruckNo ?? "") : (Truck?.PlateNo ?? "");
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? DriverDisplay => IsExternal ? ExternalDriverName : Driver?.Name;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? DriverPhoneDisplay => IsExternal ? ExternalDriverPhone : Driver?.Phone;
}

// One stop within a truck's multi-drop trip.
// One stop within a truck's multi-drop trip.
public class TruckLoadDrop
{
    public int Id { get; set; }
    public int TruckLoadId { get; set; }
    public TruckLoad? TruckLoad { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }
    public DateOnly? ActualDropOffDate { get; set; }
}

// A portion of one WarehouseAllocation being physically carried in this
// drop — supports splitting one allocation across multiple truck trips.
// InHouse/ParallelMarket prices are simple comparison figures only, no
// operational cost modeling.
public class TruckLoadItem
{
    public int Id { get; set; }
    public int TruckLoadDropId { get; set; }
    public TruckLoadDrop? TruckLoadDrop { get; set; }
    public int WarehouseAllocationId { get; set; }
    public WarehouseAllocation? WarehouseAllocation { get; set; }
    public decimal Qty { get; set; }
    public decimal? InHousePrice { get; set; }
    public decimal? ParallelMarketPrice { get; set; }
}
