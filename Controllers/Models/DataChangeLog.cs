using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models;

// Audit trail for corrections made to an existing Purchase Order or Shipment
// (see RecordCorrectionsController): one row per changed field, recording who
// changed what, when, and the old -> new value (lookups are stored by NAME, so
// the history stays readable even if a lookup is later renamed/deleted).
public class DataChangeLog
{
    public int Id { get; set; }
    [Required, MaxLength(30)] public string EntityType { get; set; } = "";   // "PurchaseOrder" | "Shipment"
    public int EntityId { get; set; }
    [MaxLength(60)] public string EntityRef { get; set; } = "";              // PO number / BL-AWB no. at the time
    [Required, MaxLength(80)] public string FieldName { get; set; } = "";
    [MaxLength(500)] public string? OldValue { get; set; }
    [MaxLength(500)] public string? NewValue { get; set; }
    [MaxLength(450)] public string ChangedByUserId { get; set; } = "";
    [MaxLength(200)] public string ChangedByName { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
