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
    public
