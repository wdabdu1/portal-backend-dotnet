using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Models.Clearance;

// One row per (this Route 3 withdrawal, deposited line item, qty taken).
// Balance remaining on a deposited item = its original QtyInBl minus the
// sum of every ClearanceRoute3Withdrawal row referencing it, across all
// withdrawal shipments over time — never a single running total field,
// so it's always correct even with concurrent/partial withdrawals.
public class ClearanceRoute3Withdrawal
{
    public int Id { get; set; }
    public int ClearanceRoute3DetailsId { get; set; }
    public ClearanceRoute3Details? ClearanceRoute3Details { get; set; }

    public int DepositShipmentLineItemId { get; set; }
    public ShipmentLineItem? DepositShipmentLineItem { get; set; }

    public decimal Qty { get; set; }
}
