using System.ComponentModel.DataAnnotations;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Orders;

public enum OrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Cancelled = 2
}

public class PurchaseOrder
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string PoNumber { get; set; } = "";

    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public int DivisionId { get; set; }
    public Division? Division { get; set; }
    public int SupplierId { get; set; }
    public BusinessPartner? Supplier { get; set; }
    public int BrandManufacturerId { get; set; }
    public BusinessPartner? BrandManufacturer { get; set; }
    public int ApprovalTypeId { get; set; }
    public ApprovalType? ApprovalType { get; set; }
    public int ConsigneeId { get; set; }
    public BusinessPartner? Consignee { get; set; }

    [MaxLength(60)] public string? SupplierPiNo { get; set; }
    public DateOnly? SupplierPiDate { get; set; }
    public int SupplierPaymentTermId { get; set; }
    public PaymentTerm? SupplierPaymentTerm { get; set; }
    public int IncotermId { get; set; }
    public Incoterm? Incoterm { get; set; }
    public int OriginCountryId { get; set; }
    public OriginCountry? OriginCountry { get; set; }
    public decimal? BuShippingBudget { get; set; }
    public int ShipmentModeId { get; set; }
    public ShipmentMode? ShipmentMode { get; set; }
    [MaxLength(60)] public string? OffshorePoNo { get; set; }
    public DateOnly? OffshorePoDate { get; set; }

    public DateOnly? ReceivedSignedPiDate { get; set; }
    public DateOnly? SentSignedPiDate { get; set; }
    public DateOnly? BuPoDate { get; set; }
    public DateOnly? OrderExecutionDate { get; set; }
    public DateOnly? LatestShippingDate { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrderLineItem> LineItems { get; set; } = new List<PurchaseOrderLineItem>();
    public ICollection<PurchaseOrderOffshorePartner> OffshorePartners { get; set; } = new List<PurchaseOrderOffshorePartner>();
}

public class PurchaseOrderLineItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public int ProductCategoryId { get; set; }
    public ProductCategory? ProductCategory { get; set; }
    public int ModelProductId { get; set; }
    public ModelProduct? ModelProduct { get; set; }
    public int ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public decimal Qty { get; set; }
    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure? UnitOfMeasure { get; set; }
    public decimal UnitPrice { get; set; }
    public int CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal Total { get; set; }
    public decimal TotalUsd { get; set; }
}

public class PurchaseOrderOffshorePartner
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public int BusinessPartnerId { get; set; }
    public BusinessPartner? BusinessPartner { get; set; }
    public int SequenceOrder { get; set; }
}
