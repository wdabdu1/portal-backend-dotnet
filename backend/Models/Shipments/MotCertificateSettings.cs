namespace ShippingPortal.Api.Models.Shipments;

// Single-row settings for the MOT Certificates monitoring dashboard.
// Always exactly one row (get-or-create in the controller, same idiom as
// LogisticsVisibilitySettings) — ExpiryDays is how long a MOT-approved PI
// (ShipmentMot.ApprovalDate / OffshoreApprovedPiNumber) is considered valid
// before it needs renewal. Currently a flat 90 days for every shipment;
// if this ever needs to vary (e.g. by BU or product category), it'll need
// to become a keyed table instead of a single row — not needed yet.
public class MotCertificateSettings
{
    public int Id { get; set; }
    public int ExpiryDays { get; set; } = 90;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
