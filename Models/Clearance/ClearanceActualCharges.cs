namespace ShippingPortal.Api.Models.Clearance;

// Forecast fields are captured ONCE — the moment Truck & Containers'
// own Actual Completion Date is first saved — using whatever the SLA
// engine's projected completion date was at that exact instant. This
// deliberately freezes "what we expected to pay, given the plan," so it
// can be honestly compared against Actual Paid later without silently
// drifting if someone updates the portal late or the shipment closes
// out well ahead of or behind schedule.
public class ClearanceActualCharges
{
    public int Id { get; set; }
    public int ClearanceId { get; set; }
    public Clearance? Clearance { get; set; }

    public decimal? ForecastDemurrageSdg { get; set; }
    public decimal? ForecastStorageSdg { get; set; }
    public DateTime? ForecastCapturedAt { get; set; }

    public decimal? ActualDemurragePaidSdg { get; set; }
    public decimal? ActualStoragePaidSdg { get; set; }

    // Moved here from Truck & Containers — fits better alongside the
    // other actual-charges figures than under the completion step.
    public DateOnly? ShippingLineDepositReturnDate { get; set; }
    public decimal? AmountReturnedFromDeposit { get; set; }
}
