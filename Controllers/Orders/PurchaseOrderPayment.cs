using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShippingPortal.Api.Models.Orders;

// One payment made (or planned) against a whole Purchase Order — an
// advance, a pre-dispatch balance, a lump sum, etc. A PO can have any
// number of these. Entered BY VALUE in a chosen currency (normally the
// PO's own currency); the % of the PO is always derived from the value,
// never typed. Each payment cascades down to every linked shipment as
// its own "from PO" line in that shipment's Payment Due Schedule
// (see PoAdvancePaymentService).
public class PurchaseOrderPayment
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }

    // Short unique-per-PO name, e.g. "Advance", "Pre-dispatch balance".
    [Required, MaxLength(60)] public string Description { get; set; } = "";

    // Amount exactly as entered, in CurrencyId.
    [Column(TypeName = "decimal(18,4)")] public decimal Amount { get; set; }
    public int CurrencyId { get; set; }

    // Amount converted to USD at the FX rate current when it was saved —
    // frozen, so a later rate change never silently moves a payment.
    [Column(TypeName = "decimal(18,4)")] public decimal AmountUsd { get; set; }

    public DateOnly? PlannedDate { get; set; }
    public DateOnly? ExecutedDate { get; set; }

    [MaxLength(255)] public string? Note { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
