using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Models.Lookups;

public class BusinessUnit
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<Division> Divisions { get; set; } = new List<Division>();
}

public class Division
{
    public int Id { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ProductCategory
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;

    // Drives which Shipping Line demurrage tariff applies — e.g. Chemical
    // goods vs Standard goods, regardless of which Business Unit ships them.
    public int? TariffGroupId { get; set; }
    public TariffGroup? TariffGroup { get; set; }
}

public class TariffGroup
{
    public int Id { get; set; }
    [Required, MaxLength(60)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ProductType
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ModelProduct
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public int? ProductCategoryId { get; set; }
    public ProductCategory? ProductCategory { get; set; }
    public int? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ApprovalType
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class PaymentTerm
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Incoterm
{
    public int Id { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = "";
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class OriginCountry
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class UnitOfMeasure
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ShipmentMode
{
    public int Id { get; set; }
    [Required, MaxLength(60)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Currency
{
    public int Id { get; set; }
    [Required, MaxLength(10)] public string Code { get; set; } = "";
    [Required, MaxLength(60)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Courier
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Forwarder
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Tenor
{
    public int Id { get; set; }
    public int Days { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SenderBank
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public decimal ChargeRate { get; set; }
    public decimal MinimumChargeAed { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ReceiverBank
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public decimal BankChargeRate { get; set; }
    public decimal ImChargeRate { get; set; }
    public decimal TotalChargeRate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ShipmentDestination
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    public bool IsFreeZone { get; set; }
    public int DefaultDurationDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PublicHoliday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public bool AffectsDxb { get; set; } = true;
    public bool AffectsClr { get; set; } = true;
}

public class FxRate
{
    public int Id { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal RateToUsd { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

public class SpcRate
{
    public int Id { get; set; }
    public decimal EuroToSdgRate { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

// Fixed 3-tier structure (Tarif-1/2/3) — same "predefined process, editable
// values" pattern as Clearance SLA. DurationDays is null for the final,
// open-ended tier. Rates are in SPC Euro per FCL per day.
public class SpcStorageTier
{
    public int Id { get; set; }
    public int TierOrder { get; set; }
    public string Label { get; set; } = "";
    public int? DurationDays { get; set; }
    public decimal Rate20 { get; set; }
    public decimal Rate40 { get; set; }
}

public class AcdCostSetting
{
    public int Id { get; set; }
    public decimal Rate20Usd { get; set; }
    public decimal Rate40Usd { get; set; }
    public DateOnly EffectiveDate { get; set; }
}
