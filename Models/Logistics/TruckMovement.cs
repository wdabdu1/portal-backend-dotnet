using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Logistics;

// A manual relocation of a truck outside the normal load/drop workflow —
// e.g. sent for service, or carrying a one-off external job not tracked
// elsewhere in the portal. Simple log entry, no approval required. This is
// also what gives the Truck Availability screen its history, not just its
// current state.
public class TruckMovement
{
    public int Id { get; set; }
    public int TruckId { get; set; }
    public Truck? Truck { get; set; }

    public int? FromCityId { get; set; }
    public LogisticsCity? FromCity { get; set; }
    public int ToCityId { get; set; }
    public LogisticsCity? ToCity { get; set; }

    public DateOnly MoveDate { get; set; }
    public string? Reason { get; set; }
    public decimal? Value { get; set; }
    public string? Notes { get; set; }

    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
