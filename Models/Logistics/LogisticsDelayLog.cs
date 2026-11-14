using System.ComponentModel.DataAnnotations;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Logistics;

// One logged hold-up in getting a shipment's cargo onto trucks after Clearance
// has released it (no truck available, fuel shortage, breakdown, ...). A shipment
// can have many entries. ToDate null = still ongoing (days are counted up to today).
// WithdrawalId is only filled when the entry was logged against one specific FZ
// withdrawal of that deposit shipment; it is a plain column on purpose (no FK).
public class LogisticsDelayLog
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }
    public Shipment? Shipment { get; set; }
    public int? WithdrawalId { get; set; }

    public int ReasonId { get; set; }
    public LogisticsDelayReason? Reason { get; set; }

    public DateOnly FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    [MaxLength(500)] public string? Note { get; set; }

    [MaxLength(256)] public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(256)] public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
