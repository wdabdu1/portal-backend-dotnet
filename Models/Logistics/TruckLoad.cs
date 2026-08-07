using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Logistics;

// One truck, one trip, one date — can carry items for multiple drops
// (multi-drop). Driver is snapshotted here (not just read live off Truck)
// so history stays accurate even if the truck's assigned driver changes later.
public class TruckLoadDrop
{
    public int Id { get; set; }
    public int TruckLoadId { get; set; }
    public TruckLoad? TruckLoad { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }
}

// One stop within a truck's multi-drop trip.
public class TruckLoadDrop
{
    public int Id { get; set; }
    public int TruckLoadId { get; set; }
    public TruckLoad? TruckLoad { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
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
