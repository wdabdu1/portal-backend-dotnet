using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models.Lookups;

public class BusinessPartner
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public bool IsSupplier { get; set; }
    public bool IsConsignee { get; set; }
    public bool IsBrandManufacturer { get; set; }
    public bool IsOffshoreEntity { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ShippingLine
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<ShippingLineDemurrageTariff> DemurrageTariffs { get; set; }
        = new List<ShippingLineDemurrageTariff>();
}

// Per shipping line, demurrage tariffs differ by the goods' Tariff Group
// (read from the shipment's Product Category — e.g. Chemical vs Standard)
// and by container size, each with a free-period day count and separate
// first-period / afterward daily rates, in SDG.
public class ShippingLineDemurrageTariff
{
    public int Id { get; set; }
    public int ShippingLineId { get; set; }
    public ShippingLine? ShippingLine { get; set; }

    public int TariffGroupId { get; set; }
    public TariffGroup? TariffGroup { get; set; }

    [Required, MaxLength(5)] public string ContainerSize { get; set; } = ""; // "20" or "40"

    public int FreeDays { get; set; }
    public int FirstPeriodDays { get; set; }
    public decimal FirstPeriodRateSdg { get; set; }
    public decimal AfterwardRateSdg { get; set; }
}
}
