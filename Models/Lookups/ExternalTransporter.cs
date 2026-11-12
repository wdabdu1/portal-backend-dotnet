using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models.Lookups;

// Third-party transport companies used when a truck load is NOT carried by
// the in-house fleet (Logistics -> Truck Allocation -> "External"). Only the
// company is a maintained list; the truck number and driver name/phone are
// typed in on every load, because those change per trip.
public class ExternalTransporter
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(60)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
