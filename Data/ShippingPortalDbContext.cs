using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;

namespace ShippingPortal.Api.Data;

public class ShippingPortalDbContext : IdentityDbContext<ApplicationUser>
{
    public ShippingPortalDbContext(DbContextOptions<ShippingPortalDbContext> options)
        : base(options)
    {
    }

    // Identity / access
    public DbSet<UserBusinessUnitAccess> UserBusinessUnitAccess => Set<UserBusinessUnitAccess>();

    // Settings / master data
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

    // Orders
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLineItem> PurchaseOrderLineItems => Set<PurchaseOrderLineItem>();
    public DbSet<PurchaseOrderOffshorePartner> PurchaseOrderOffshorePartners => Set<PurchaseOrderOffshorePartner>();

    // Shipments
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLineItem> ShipmentLineItems => Set<ShipmentLineItem>();
    public DbSet<ShipmentForwarder> ShipmentForwarders => Set<ShipmentForwarder>();
    public DbSet<ShipmentAcd> ShipmentAcds => Set<ShipmentAcd>();
    public DbSet<ShipmentDraftDocuments> ShipmentDraftDocuments => Set<ShipmentDraftDocuments>();
    public DbSet<ShipmentSsmo> ShipmentSsmos => Set<ShipmentSsmo>();
    public DbSet<ShipmentMot> ShipmentMots => Set<ShipmentMot>();
    public DbSet<ShipmentSupplierFullSet> ShipmentSupplierFullSets => Set<ShipmentSupplierFullSet>();
    public DbSet<ShipmentSupplierPayment> ShipmentSupplierPayments => Set<ShipmentSupplierPayment>();
    public DbSet<ShipmentBanking> ShipmentBankings => Set<ShipmentBanking>();

    // Clearance
    public DbSet<ShippingPortal.Api.Models.Clearance.Clearance> Clearances => Set<ShippingPortal.Api.Models.Clearance.Clearance>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceSlaSetting> ClearanceSlaSettings => Set<ShippingPortal.Api.Models.Clearance.ClearanceSlaSetting>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceRoute1Details> ClearanceRoute1Details => Set<ShippingPortal.Api.Models.Clearance.ClearanceRoute1Details>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceRoute2Details> ClearanceRoute2Details => Set<ShippingPortal.Api.Models.Clearance.ClearanceRoute2Details>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceRoute3Details> ClearanceRoute3Details => Set<ShippingPortal.Api.Models.Clearance.ClearanceRoute3Details>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceDeliveryOrder> ClearanceDeliveryOrders => Set<ShippingPortal.Api.Models.Clearance.ClearanceDeliveryOrder>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceCostEstimate> ClearanceCostEstimates => Set<ShippingPortal.Api.Models.Clearance.ClearanceCostEstimate>();
    public DbSet<ShippingPortal.Api.Models.Clearance.ClearanceCertificateEntry> ClearanceCertificateEntries => Set<ShippingPortal.Api.Models.Clearance.ClearanceCertificateEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Business keys
        builder.Entity<PurchaseOrder>().HasIndex(p => p.PoNumber).IsUnique();
        builder.Entity<Shipment>().HasIndex(s => s.BlAwbNo).IsUnique();

        // Restrict-delete on lookups referenced by transactional data, so a
        // Settings row can't be deleted out from under an existing PO/Shipment.
        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.Supplier)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.BrandManufacturer)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.Consignee)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderOffshorePartner>()
            .HasOne(o => o.BusinessPartner)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        // Line items cascade with their parent order/shipment.
        builder.Entity<PurchaseOrderLineItem>()
            .HasOne(li => li.PurchaseOrder)
            .WithMany(p => p.LineItems)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseOrderOffshorePartner>()
            .HasOne(o => o.PurchaseOrder)
            .WithMany(p => p.OffshorePartners)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShipmentLineItem>()
            .HasOne(li => li.Shipment)
            .WithMany(s => s.LineItems)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShipmentLineItem>()
            .HasOne(li => li.PurchaseOrderLineItem)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<UserBusinessUnitAccess>()
            .HasOne(a => a.User)
            .WithMany(u => u.BusinessUnitAccess)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserBusinessUnitAccess>()
            .HasIndex(a => new { a.UserId, a.BusinessUnitId })
            .IsUnique();

        // 1:1 Shipment sub-groups, each independently editable.
        builder.Entity<ShipmentForwarder>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentAcd>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentDraftDocuments>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSsmo>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentMot>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSupplierFullSet>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentSupplierPayment>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShipmentBanking>().HasIndex(x => x.ShipmentId).IsUnique();

        // Clearance: 1:1 with Shipment, and each route/sub-section 1:1 with Clearance.
        builder.Entity<ShippingPortal.Api.Models.Clearance.Clearance>().HasIndex(x => x.ShipmentId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceRoute1Details>().HasIndex(x => x.ClearanceId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceRoute2Details>().HasIndex(x => x.ClearanceId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceRoute3Details>().HasIndex(x => x.ClearanceId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceDeliveryOrder>().HasIndex(x => x.ClearanceId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceCostEstimate>().HasIndex(x => x.ClearanceId).IsUnique();
        builder.Entity<ShippingPortal.Api.Models.Clearance.ClearanceCertificateEntry>().HasIndex(x => x.ClearanceId).IsUnique();
    }
}
