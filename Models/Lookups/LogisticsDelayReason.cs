using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models.Lookups;

// Maintained list of reasons for the Logistics Delay Log
// (Logistics - Settings > Delay Reasons).
public class LogisticsDelayReason
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
