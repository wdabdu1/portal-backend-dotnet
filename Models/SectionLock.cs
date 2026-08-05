namespace ShippingPortal.Api.Models;

// Generic per-section lock, reusable across any screen with independently
// saved sections (Shipment's Forwarder/ACD/Banking/etc., Clearance's
// General Info/Route/Cost Estimate/route-group items/etc.). Once confirmed,
// the section's own save endpoint rejects further edits until a
// Manager/SuperUser explicitly unlocks it again.
public class SectionLock
{
    public int Id { get; set; }
    public string EntityType { get; set; } = ""; // "Shipment" or "Clearance"
    public int EntityId { get; set; }
    public string SectionKey { get; set; } = "";

    public string ConfirmedByUserId { get; set; } = "";
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}
