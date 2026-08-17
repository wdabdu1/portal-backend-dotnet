using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models;

// One row per offshore BusinessPartner — the markup % and currency
// that pre-fill on the TP screen the first time that company appears
// in a chain, so Finance doesn't have to re-enter the same figures
// shipment after shipment. Purely a starting point: the TP screen's
// own saved TransferPricingEntry always wins once the user has
// actually entered or edited a value for a specific shipment.
public class OffshoreMarkupDefault
{
    public int Id { get; set; }

    public int BusinessPartnerId { get; set; }
    public BusinessPartner? BusinessPartner { get; set; }

    public decimal DefaultMarkupPercent { get; set; }

    public int DefaultCurrencyId { get; set; }
    public Currency? DefaultCurrency { get; set; }
}
