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

public class ShippingLineDemurrageTariff
{
    public int Id { get; set; }
    public int ShippingLineId { get; set; }
    public ShippingLine? ShippingLine { get; set; }
    [Required, MaxLength(30)] public string BuGroup { get; set; } = "";
    [Required, MaxLength(5)] public string ContainerSize { get; set; } = "";
    public int FreeDays { get; set; }
    public decimal FirstPeriodRate { get; set; }
    public decimal AfterwardRate { get; set; }
}
