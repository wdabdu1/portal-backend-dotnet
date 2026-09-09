namespace ShippingPortal.Api.Models.Logistics;

// Single-row settings controlling what Logistics/Coordinator can see, and
// when — the confidentiality-tiering redesign for the Logistics module.
// Always exactly one row (seeded by migration); the controller
// get-or-creates defensively in case a fresh environment never ran the
// seed insert.
public class LogisticsVisibilitySettings
{
    public int Id { get; set; }

    // Days before vessel ETA that a shipment first becomes visible at all
    // to Logistics/Coordinator — before this, it doesn't appear in their
    // queue. Same value for both roles/routes.
    public int ArrivalLeadTimeDays { get; set; } = 14;

    // Days before Clearance completion that Category+Quantity reveals for
    // a Route 1 De-stuff shipment (Route 1 only — Route 3/FZ withdrawals
    // have no container tier to begin with and use this same window
    // differently, see LogisticsController). Coordinator-editable;
    // Logistics sees it read-only.
    public int PreClearanceCatQtyRevealDays { get; set; } = 7;

    // Days after Delivered/Completed that Category+Quantity re-hides again.
    // Full truck/trip history (counts, dates, Internal/External, prices)
    // is never deleted — only this granularity re-hides.
    public int PostDeliveryRehideDays { get; set; } = 30;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
