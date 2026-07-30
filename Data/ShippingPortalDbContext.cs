using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Data;

public class ShippingPortalDbContext : IdentityDbContext<ApplicationUser>
{
    public ShippingPortalDbContext(DbContextOptions<ShippingPortalDbContext> options) : base(options) { }

    public DbSet<UserBusinessUnitAccess> UserBusinessUnitAccess => Set<UserBusinessUnitAccess>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductType> ProductTypes => Set<ProductType>();
    public DbSet<ModelProduct> ModelProducts => Set<ModelProduct>();
    public DbSet<ApprovalType> ApprovalTypes => Set<ApprovalType>();
    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();
    public DbSet<Incoterm> Incoterms => Set<Incoterm>();
    public DbSet<OriginCountry> OriginCountries => Set<OriginCountry>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<ShipmentMode> ShipmentModes => Set<ShipmentMode>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<Forwarder> Forwarders => Set<Forwarder>();
    public DbSet<Tenor> Tenors => Set<Tenor>();
    public DbSet<SenderBank> SenderBanks => Set<SenderBank>();
    public DbSet<ReceiverBank> ReceiverBanks => Set<ReceiverBank>();
    public DbSet<ShipmentDestination> ShipmentDestinations => Set<ShipmentDestination>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<SpcRate> SpcRates => Set<SpcRate>();
    public DbSet<AcdCostSetting> AcdCostSettings => Set<AcdCostSetting>();
    public DbSet<ShippingLine> ShippingLines => Set<ShippingLine>();
    public DbSet<ShippingLineDemurrageTariff> ShippingLineDemurrageTariffs => Set<ShippingLineDemurrageTariff>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLineItem> PurchaseOrderLineItems => Set<PurchaseOrderLineItem>();
    public DbSet<PurchaseOrderOffshorePartner> PurchaseOrderOffshorePartners => Set<PurchaseOrderOffshorePartner>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLineItem> ShipmentLineItems => Set<ShipmentLineItem>();
    public DbSet<ShipmentLineItem> ShipmentLineItems => Set<ShipmentLineItem>();
    public DbSet<ShipmentForwarder> ShipmentForwarders => Set<ShipmentForwarder>();
    public DbSet<ShipmentAcd> ShipmentAcds => Set<ShipmentAcd>();
    public DbSet<ShipmentDraftDocuments> ShipmentDraftDocuments => Set<ShipmentDraftDocuments>();
    public DbSet<ShipmentSsmo> ShipmentSsmos => Set<ShipmentSsmo>();
    public DbSet<ShipmentMot> ShipmentMots => Set<ShipmentMot>();
    public DbSet<ShipmentSupplierFullSet> ShipmentSupplierFullSets => Set<ShipmentSupplierFullSet>();
    public DbSet<ShipmentSupplierPayment> ShipmentSupplierPayments => Set<ShipmentSupplierPayment>();
    public DbSet<ShipmentBanking> ShipmentBankings => Set<ShipmentBanking>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PurchaseOrder>().HasIndex(p => p.PoNumber).IsUnique();
        builder.Entity<Shipment>().HasIndex(s => s.BlAwbNo).IsUnique();

        builder.Entity<PurchaseOrder>().HasOne(p => p.Supplier).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseOrder>().HasOne(p => p.BrandManufacturer).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseOrder>().HasOne(p => p.Consignee).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseOrderOffshorePartner>().HasOne(o => o.BusinessPartner).WithMany().OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderLineItem>().HasOne(li => li.PurchaseOrder).WithMany(p => p.LineItems).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PurchaseOrderOffshorePartner>().HasOne(o => o.PurchaseOrder).WithMany(p => p.OffshorePartners).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ShipmentLineItem>().HasOne(li => li.Shipment).WithMany(s => s.LineItems).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ShipmentLineItem>().HasOne(li => li.PurchaseOrderLineItem).WithMany().OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserBusinessUnitAccess>().HasOne(a => a.User).WithMany(u => u.BusinessUnitAccess).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<UserBusinessUnitAccess>().HasIndex(a => new { a.UserId, a.BusinessUnitId }).IsUnique();

        // 1:1 Shipment sub-groups, each independently editable.
        builder.Entity<ShipmentForwarder>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentAcd>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentDraftDocuments>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSsmo>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentMot>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSupplierFullSet>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSupplierPayment>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentBanking>().HasIndex(x => x.ShipmentId).IsUnique();
    }
}
