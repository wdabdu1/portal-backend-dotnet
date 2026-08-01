using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models.Clearance;

// Open Settings list (Manager/SuperUser can add new charge types over
// time), unlike the fixed Clearance SLA process list.
public class ClearanceChargeType
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
