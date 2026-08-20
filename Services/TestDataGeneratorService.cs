using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Logistics;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Services;

// One-time, deliberately hand-built generator that creates a realistic,
// fully interlinked test dataset — POs at every stage of execution,
// Shipments at every document/clearance stage, real Cost Estimates,
// completed FZ deposits, and truck allocations — using the 7 real
// BU/Division/Supplier/OffshoreChain/Consignee combinations already in
// Settings. Not exposed for repeated casual use; meant to be run once
// against a clean, real Settings baseline.
public class TestDataGeneratorService
{
    private readonly ShippingPortalDbContext _db;
    private readonly Random _rng = new Random(42); // fixed seed — reproducible runs
    public TestDataGeneratorService(ShippingPortalDbContext db) => _db = db;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Family(
        string BuCode, string DivisionCode, string SupplierName, string? BrandName,
        string[] OffshoreChain, string ConsigneeName);

    private static readonly Family[] Families =
    {
        new("B01", "DRE", "LONG FAT GLOBAL CO., LTD", "FORWARD (BEIHAI) HEPU PRESTICIDE CO. LTD", new[] { "Taif", "Cencom" }, "Digitech"),
        new("B01", "DEM", "LG", null, new[] { "Taif", "Techuip" }, "Digitech"),
        new("B01", "Delta", "Homa", null, new[] { "Taif", "Techuip", "DELTAFZ_PS" }, "Delta Trading"),
        new("B01", "Digitech", "Teval", null, new[] { "Taif", "Techuip", "CADFZ_PS" }, "Digitech"),
        new("B03", "GCROP", "BASF SE", null, new[] { "Taif", "Cencom" }, "GREEN CROP AGRI"),
        new("B03", "Agro", "UPL LIMITED", null, new[] { "Taif", "Cencom", "Techuip" }, "CTC AGROCHEMICALS"),
        new("B02", "Agri", "CNH", null, new[] { "Cencom" }, "Central Trading Co."),
    };

    private class Lookups
    {
        public Dictionary<string, BusinessUnit> BusinessUnits = new();
        public Dictionary<(string Bu, string Div), Division> Divisions = new();
        public Dictionary<string, BusinessPartner> Partners = new();
        public Currency Usd = null!;
        public Currency Sdg = null!;
        public List<ProductCategory> Categories = new();
        public List<ModelProduct> Models = new();
        public List<ProductType> Types = new();
        public List<UnitOfMeasure> Uoms = new();
        public ApprovalType Approval = null!;
        public PaymentTerm PaymentTerm = null!;
        public Incoterm Incoterm = null!;
        public OriginCountry Origin = null!;
        public ShipmentMode SeaMode = null!;
        public List<ShippingLine> ShippingLines = new();
        public ShipmentDestination Psdfz = null!;
        public ShipmentDestination GarriFz = null!;
        public List<ClearanceChargeType> ChargeTypes = new();
        public List<Driver> Drivers = new();
        public List<Truck> Trucks = new();
        public List<Warehouse> Warehouses = new();
    }

    private async Task<Lookups> LoadLookups()
    {
        var lk = new Lookups();
        var bus = await _db.BusinessUnits.ToListAsync();
        lk.BusinessUnits = bus.ToDictionary(b => b.Code);

        var divs = await _db.Divisions.ToListAsync();
        foreach (var bu in bus)
            foreach (var d in divs.Where(x => x.BusinessUnitId == bu.Id))
                lk.Divisions[(bu.Code, d.Code)] = d;

        lk.Partners = (await _db.BusinessPartners.ToListAsync()).ToDictionary(p => p.Name);

        var currencies = await _db.Currencies.ToListAsync();
        lk.Usd = currencies.First(c => c.Code == "USD");
        lk.Sdg = currencies.First(c => c.Code == "SDG");

        lk.Categories = await _db.ProductCategories.Where(c => c.IsActive).ToListAsync();
        lk.Models = await _db.ModelProducts.Where(m => m.IsActive).ToListAsync();
        lk.Types = await _db.ProductTypes.Where(t => t.IsActive).ToListAsync();
        lk.Uoms = await _db.UnitsOfMeasure.Where(u => u.IsActive).ToListAsync();
        lk.Approval = (await _db.ApprovalTypes.FirstOrDefaultAsync(a => a.IsActive))!;
        lk.PaymentTerm = (await _db.PaymentTerms.FirstOrDefaultAsync(p => p.IsActive))!;
        lk.Incoterm = (await _db.Incoterms.FirstOrDefaultAsync(i => i.IsActive))!;
        lk.Origin = (await _db.OriginCountries.FirstOrDefaultAsync(o => o.IsActive))!;
        lk.SeaMode = (await _db.ShipmentModes.FirstOrDefaultAsync(m => m.IsActive))!;
        lk.ShippingLines = await _db.ShippingLines.Where(s => s.IsActive).ToListAsync();
        lk.Psdfz = (await _db.ShipmentDestinations.FirstAsync(d => d.Name == "PSDFZ"));
        lk.GarriFz = (await _db.ShipmentDestinations.FirstAsync(d => d.Name == "Garri FZ"));
        lk.ChargeTypes = await _db.ClearanceChargeTypes.Where(c => c.IsActive).ToListAsync();
        lk.Drivers = await _db.Drivers.Where(d => d.IsActive).ToListAsync();
        lk.Trucks = await _db.Trucks.Where(t => t.IsActive).ToListAsync();
        lk.Warehouses = await _db.Warehouses.Where(w => w.IsActive).ToListAsync();

        return lk;
    }

    private T Pick<T>(List<T> list) => list[_rng.Next(list.Count)];
  
    // ---------- PO / Shipment creation ----------

    private async Task<PurchaseOrder> CreatePoAsync(Lookups lk, Family fam, string poNumber, int placedDaysAgo, OrderStatus status)
    {
        var bu = lk.BusinessUnits[fam.BuCode];
        var division = lk.Divisions[(fam.BuCode, fam.DivisionCode)];
        var supplier = lk.Partners[fam.SupplierName];
        var brand = fam.BrandName is not null ? lk.Partners[fam.BrandName] : supplier;
        var consignee = lk.Partners[fam.ConsigneeName];

        var poDate = Today.AddDays(-placedDaysAgo);
        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            BusinessUnitId = bu.Id,
            DivisionId = division.Id,
            SupplierId = supplier.Id,
            BrandManufacturerId = brand.Id,
            ApprovalTypeId = lk.Approval.Id,
            ConsigneeId = consignee.Id,
            SupplierPiNo = $"PI-{poNumber}",
            SupplierPiDate = poDate.AddDays(-3),
            SupplierPaymentTermId = lk.PaymentTerm.Id,
            ReceivedSignedPiDate = poDate.AddDays(-2),
            SentSignedPiDate = poDate.AddDays(-1),
            BuPoDate = poDate,
            OrderExecutionDate = poDate.AddDays(2),
            LatestShippingDate = poDate.AddDays(45),
            IncotermId = lk.Incoterm.Id,
            OriginCountryId = lk.Origin.Id,
            ShipmentModeId = lk.SeaMode.Id,
            BuShippingBudget = 15000 + _rng.Next(0, 10000),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return po;
    }

    private async Task<List<PurchaseOrderLineItem>> AddPoLineItemsAsync(Lookups lk, PurchaseOrder po, int count)
    {
        var lines = new List<PurchaseOrderLineItem>();
        for (int i = 0; i < count; i++)
        {
            var model = Pick(lk.Models);
            var qty = 100 + _rng.Next(0, 400);
            var unitPrice = 50 + _rng.Next(0, 450);
            var line = new PurchaseOrderLineItem
            {
                PurchaseOrderId = po.Id,
                ProductCategoryId = model.ProductCategoryId ?? Pick(lk.Categories).Id,
                ModelProductId = model.Id,
                ProductTypeId = model.ProductTypeId ?? Pick(lk.Types).Id,
                Qty = qty,
                UnitOfMeasureId = Pick(lk.Uoms).Id,
                UnitPrice = unitPrice,
                CurrencyId = lk.Usd.Id,
                Total = qty * unitPrice,
                TotalUsd = qty * unitPrice
            };
            _db.PurchaseOrderLineItems.Add(line);
            lines.Add(line);
        }
        await _db.SaveChangesAsync();
        return lines;
    }

    private async Task<Shipment> CreateShipmentAsync(Lookups lk, PurchaseOrder po, string blAwbNo,
        DateOnly blAwbDate, DateOnly eta, ShipmentStatus status, bool marineInsurance)
    {
        var line = lk.ShippingLines.Count > 0 ? Pick(lk.ShippingLines) : null;
        var ship = new Shipment
        {
            PurchaseOrderId = po.Id,
            BlAwbNo = blAwbNo,
            BlAwbDate = blAwbDate,
            Etd = blAwbDate.AddDays(2),
            Eta = eta,
            Status = status,
            ShippingLineId = line?.Id ?? 1,
            Fcl20Count = 1 + _rng.Next(0, 2),
            Fcl40Count = _rng.Next(0, 2),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Shipments.Add(ship);
        await _db.SaveChangesAsync();
        return ship;
    }

    private async Task<List<ShipmentLineItem>> AddShipmentLineItemsAsync(Shipment ship, List<PurchaseOrderLineItem> poLines)
    {
        var result = new List<ShipmentLineItem>();
        foreach (var pl in poLines)
        {
            var sl = new ShipmentLineItem
            {
                ShipmentId = ship.Id,
                PurchaseOrderLineItemId = pl.Id,
                QtyInBl = pl.Qty,
                ItemSubtotal = pl.Total
            };
            _db.ShipmentLineItems.Add(sl);
            result.Add(sl);
        }
        await _db.SaveChangesAsync();
        return result;
    }

    // ---------- Shipment document-stage helpers ----------

    private async Task AddDraftDocsAsync(Shipment ship, DateOnly blAwbDate)
    {
        _db.ShipmentDraftDocuments.Add(new ShipmentDraftDocuments
        {
            ShipmentId = ship.Id,
            InitialDraftReceivedDate = blAwbDate.AddDays(-3),
            FinalDraftConfirmedDate = blAwbDate.AddDays(-1)
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddSupplierFullSetAsync(Shipment ship, DateOnly blAwbDate)
    {
        _db.ShipmentSupplierFullSets.Add(new ShipmentSupplierFullSet
        {
            ShipmentId = ship.Id,
            SupplierInvoiceNo = $"INV-{ship.BlAwbNo}",
            SupplierInvoiceDate = blAwbDate,
            FsDispatchDate = blAwbDate.AddDays(1),
            FsTrackingNumber = $"{_rng.Next(100000000, 999999999)}",
            FsReceivedDate = blAwbDate.AddDays(7)
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddBankingAsync(Shipment ship, DateOnly blAwbDate)
    {
        _db.ShipmentBankings.Add(new ShipmentBanking
        {
            ShipmentId = ship.Id,
            OsDocDispatchDate = blAwbDate.AddDays(1),
            OsDocTrackingNumber = $"{_rng.Next(100000000, 999999999)}"
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddForwarderAsync(Shipment ship, Lookups lk, bool marineInsurance)
    {
        _db.ShipmentForwarders.Add(new ShipmentForwarder
        {
            ShipmentId = ship.Id,
            ActualShippingCost = 8000 + _rng.Next(0, 5000),
            CurrencyId = lk.Usd.Id,
            AmountSaved = _rng.Next(0, 500),
            MarineInsurance = marineInsurance
        });
        await _db.SaveChangesAsync();
    }
  
    private async Task AddAcdAsync(Shipment ship, DateOnly blAwbDate)
    {
        _db.ShipmentAcds.Add(new ShipmentAcd
        {
            ShipmentId = ship.Id,
            ProcessDate = blAwbDate.AddDays(2),
            CostUsd = 120 + _rng.Next(0, 80),
            CostSettledDate = blAwbDate.AddDays(4),
            RefNumber = $"ACD-{ship.BlAwbNo}"
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddMotAsync(Shipment ship, DateOnly blAwbDate, bool approved)
    {
        _db.ShipmentMots.Add(new ShipmentMot
        {
            ShipmentId = ship.Id,
            ProcessDate = blAwbDate.AddDays(1),
            Cost = 300 + _rng.Next(0, 200),
            CostSettledDate = approved ? blAwbDate.AddDays(3) : null,
            RefNumber = $"MOT-{ship.BlAwbNo}",
            ApprovalDate = approved ? blAwbDate.AddDays(5) : null,
            OffshoreApprovedPiNumber = approved ? $"PI-APPR-{ship.BlAwbNo}" : null
        });
        await _db.SaveChangesAsync();
    }

    // ---------- Clearance ----------

    private async Task<Clearance> CreateClearanceAsync(Shipment ship, ClearanceRouteType route, DateOnly? actualArrivalDate)
    {
        var clearance = new Clearance
        {
            ShipmentId = ship.Id,
            Route = route,
            CopyOfBlReceivedDate = actualArrivalDate?.AddDays(-2),
            ImFormNo = $"IM-{ship.BlAwbNo}",
            ImFormDate = actualArrivalDate?.AddDays(-1)
        };
        _db.Clearances.Add(clearance);
        await _db.SaveChangesAsync();
        return clearance;
    }

    private async Task AddDeliveryOrderAsync(Clearance clearance, DateOnly actualArrivalDate, bool completed)
    {
        _db.ClearanceDeliveryOrders.Add(new ClearanceDeliveryOrder
        {
            ClearanceId = clearance.Id,
            ActualArrivalDate = actualArrivalDate,
            ReceiveDoDate = actualArrivalDate.AddDays(1),
            CopyOfDoCollectedDate = actualArrivalDate.AddDays(1),
            DepositRequired = _rng.Next(0, 2) == 0,
            DoActualFeesSdg = completed ? 50000 + _rng.Next(0, 30000) : null,
            DoFeesSettledDate = completed ? actualArrivalDate.AddDays(2) : null,
            DoReceivedDate = completed ? actualArrivalDate.AddDays(2) : null
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddCertificateEntryAsync(Clearance clearance, DateOnly actualArrivalDate)
    {
        _db.ClearanceCertificateEntries.Add(new ClearanceCertificateEntry
        {
            ClearanceId = clearance.Id,
            CertificateEntryDate = actualArrivalDate.AddDays(1),
            ScudaDeclarationNo = $"SCUDA-{clearance.Id}"
        });
        await _db.SaveChangesAsync();
    }

    // One row per charge type, roughly proportional to a 100k-400k USD
    // shipment's real customs cost profile. Some marked paid, some not,
    // driven by the caller.
    private async Task AddCostEstimateAsync(Clearance clearance, Lookups lk, DateOnly estimateDate, decimal paidFraction)
    {
        _db.ClearanceCostEstimates.Add(new ClearanceCostEstimate { ClearanceId = clearance.Id, EstimateDate = estimateDate, NotifyBuDate = estimateDate });

        var weights = new Dictionary<string, decimal> { ["DO Fees"] = 0.05m, ["Ship. Line Deposit"] = 0.10m, ["Move"] = 0.03m, ["SPC"] = 0.08m, ["Customs Duties"] = 0.65m, ["SSMO"] = 0.09m };
        var baseTotal = 300000 + _rng.Next(0, 400000);
        int i = 0;
        int payCount = (int)Math.Round(lk.ChargeTypes.Count * paidFraction);
        foreach (var ct in lk.ChargeTypes)
        {
            var weight = weights.TryGetValue(ct.Name, out var w) ? w : 0.10m;
            var value = Math.Round(baseTotal * weight, 0);
            var isPaid = i < payCount;
            _db.ClearanceEstimateLineItems.Add(new ClearanceEstimateLineItem
            {
                ClearanceId = clearance.Id,
                ChargeTypeId = ct.Id,
                ValueSdg = value,
                DueDate = estimateDate.AddDays(7),
                IsPaid = isPaid,
                PaidDate = isPaid ? estimateDate.AddDays(5) : null
            });
            i++;
        }
        await _db.SaveChangesAsync();
    }

    // stepsCompleted: how many of the 8 Route 1 steps are done, in order
    // (Move, SSMO File, Cust Exam, Cust Lab, SSMO Exam, Cust Eval, SPC Bill,
    // Truck & Containers). 8 = fully cleared.
    private async Task AddRoute1ProgressAsync(Clearance clearance, DateOnly startDate, int stepsCompleted)
    {
        var r = new ClearanceRoute1Details { ClearanceId = clearance.Id };
        var d = startDate;
        if (stepsCompleted >= 1) { r.MoveRequestDate = d; r.BillAmountSdg = 15000; r.BillSettlementDate = d.AddDays(1); d = d.AddDays(1); }
        if (stepsCompleted >= 2) { r.SsmoFileRequestDate = d; r.SsmoInspectionAmountSdg = 20000; r.SsmoFeesSettlementDate = d.AddDays(2); d = d.AddDays(2); }
        if (stepsCompleted >= 3) { r.CustExamStartDate = d; r.CustExamCompletedDate = d.AddDays(2); d = d.AddDays(2); }
        if (stepsCompleted >= 4) { r.CustomsLabRequired = false; }
        if (stepsCompleted >= 5) { r.SsmoExamStartDate = d; r.SsmoCertIssuanceDate = d.AddDays(2); d = d.AddDays(2); }
        if (stepsCompleted >= 6) { r.CustEvaluationDate = d; r.CustomsDutySdg = 250000; r.CustomsSettlementDate = d.AddDays(2); r.ReleaseExitPassDate = d.AddDays(2); d = d.AddDays(2); }
        if (stepsCompleted >= 7) { r.SpcBillRequestDate = d; r.SpcBillValueSdg = 18000; r.SpcBillSettlementDate = d.AddDays(1); d = d.AddDays(1); }
        if (stepsCompleted >= 8) { r.TruckPortEntryPermitDate = d; r.ContainersReturnedDate = d.AddDays(2); r.ShippingLineDepositReturnDate = d.AddDays(3); r.DepositValue = 5000; r.ClearanceActualCompletedDate = d.AddDays(2); }
        _db.ClearanceRoute1Details.Add(r);
        await _db.SaveChangesAsync();
    }

    // For Route 2 — either mid-progress (deposit requested, not yet at FZ)
    // or genuinely completed (goods received at FZ — this is what makes a
    // shipment show up in FZ Inventory).
    private async Task AddRoute2ProgressAsync(Clearance clearance, ShipmentDestination destination, DateOnly startDate, bool depositedAtFz)
    {
        var r = new ClearanceRoute2Details
        {
            ClearanceId = clearance.Id,
            DepositRequestDate = startDate,
            RequestApprovalDate = startDate.AddDays(1),
            DepositRefNo = $"DEP-{clearance.Id}",
            FzInvoiceNo = $"FZINV-{clearance.Id}",
            DestinationId = destination.Id,
            InspectionDate = startDate.AddDays(1),
            SpcBillRequestDate = startDate.AddDays(1),
            SpcBillValueSdg = 12000,
            SpcBillSettlementDate = startDate.AddDays(2),
            PoliceSecurityAppointedDate = startDate
        };
        if (depositedAtFz)
        {
            r.TruckPortEntryPermitDate = startDate.AddDays(2);
            r.ContainersReceivedAtFzDate = startDate.AddDays(3);
        }
        _db.ClearanceRoute2Details.Add(r);
        await _db.SaveChangesAsync();
    }

    private async Task AddTruckAllocationAsync(Lookups lk, ShipmentLineItem shipLine, DateOnly expected, string status)
    {
        if (lk.Trucks.Count == 0 || lk.Warehouses.Count == 0) return;
        var truck = Pick(lk.Trucks);
        var load = new TruckLoad { TruckId = truck.Id, DriverId = truck.DriverId, LoadDate = expected.AddDays(-1), Notes = $"Auto test data ({status})" };
        _db.TruckLoads.Add(load);
        await _db.SaveChangesAsync();

        DateOnly? actual = status switch
        {
            "Arrived" => expected,
            "Delayed" => expected.AddDays(2 + _rng.Next(0, 3)),
            "OnTime" => expected,
            _ => null
        };
        var drop = new TruckLoadDrop { TruckLoadId = load.Id, WarehouseId = Pick(lk.Warehouses).Id, ExpectedDeliveryDate = expected, ActualDropOffDate = actual };
        _db.TruckLoadDrops.Add(drop);
        await _db.SaveChangesAsync();

        var allocation = new WarehouseAllocation { ShipmentLineItemId = shipLine.Id, WarehouseId = drop.WarehouseId, Qty = shipLine.QtyInBl, AllocatedAt = DateTime.UtcNow };
        _db.WarehouseAllocations.Add(allocation);
        await _db.SaveChangesAsync();

        _db.TruckLoadItems.Add(new TruckLoadItem { TruckLoadDropId = drop.Id, WarehouseAllocationId = allocation.Id, Qty = shipLine.QtyInBl });
        await _db.SaveChangesAsync();
    }
  
    // ---------- Main orchestration ----------

    public async Task<string> GenerateAsync()
    {
        var lk = await LoadLookups();
        int poCounter = 1, blCounter = 1;
        int posCreated = 0, shipmentsCreated = 0;

        string NextPo() => $"PO-T{poCounter++:D3}";
        string NextBl(string prefix) => $"BL-T{prefix}{blCounter++:D3}";

        // Round-robin through the 7 families so every PO uses real,
        // correctly-matched Supplier/Division/Offshore/Consignee data.
        Family FamAt(int i) => Families[i % Families.Length];

        // --- 3 New POs (Draft, no shipments at all) ---
        for (int i = 0; i < 3; i++)
        {
            var fam = FamAt(i);
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 3 + i, OrderStatus.Draft);
            await AddPoLineItemsAsync(lk, po, 2);
            posCreated++;
        }

        // --- 5 Partially executed POs (2-3 line items, only some shipped) ---
        for (int i = 0; i < 5; i++)
        {
            var fam = FamAt(i + 3);
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 20 + i * 3, OrderStatus.Confirmed);
            var lines = await AddPoLineItemsAsync(lk, po, 3);
            posCreated++;

            // Ship only the first line item — a genuine partial execution.
            var blDate = Today.AddDays(-(15 + i * 2));
            var ship = await CreateShipmentAsync(lk, po, NextBl("PE"), blDate, blDate.AddDays(28), ShipmentStatus.Confirmed, marineInsurance: i % 2 == 0);
            await AddShipmentLineItemsAsync(ship, new List<PurchaseOrderLineItem> { lines[0] });
            await AddDraftDocsAsync(ship, blDate);
            await AddForwarderAsync(ship, lk, marineInsurance: i % 2 == 0);
            shipmentsCreated++;
        }

        // --- 7 fully executed POs (all line items shipped across 1-2 shipments) ---
        var fullyExecutedShipments = new List<Shipment>();
        var fullyExecutedPoIndex = new List<Family>();
        for (int i = 0; i < 7; i++)
        {
            var fam = FamAt(i);
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 40 + i * 4, OrderStatus.Confirmed);
            var lines = await AddPoLineItemsAsync(lk, po, 2);
            posCreated++;

            var blDate = Today.AddDays(-(35 + i * 4));
            var ship = await CreateShipmentAsync(lk, po, NextBl("FE"), blDate, blDate.AddDays(28), ShipmentStatus.Confirmed, marineInsurance: i % 3 != 0);
            await AddShipmentLineItemsAsync(ship, lines);
            await AddDraftDocsAsync(ship, blDate);
            await AddSupplierFullSetAsync(ship, blDate);
            await AddBankingAsync(ship, blDate);
            await AddForwarderAsync(ship, lk, marineInsurance: i % 3 != 0);
            await AddAcdAsync(ship, blDate);
            await AddMotAsync(ship, blDate, approved: i % 4 != 3); // 1 in 4 stuck on MOT
            shipmentsCreated++;
            fullyExecutedShipments.Add(ship);
            fullyExecutedPoIndex.Add(fam);
        }

        // --- 5 New shipments (just created, no clearance yet) ---
        for (int i = 0; i < 5; i++)
        {
            var fam = FamAt(i + 2);
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 5, OrderStatus.Confirmed);
            var lines = await AddPoLineItemsAsync(lk, po, 1);
            posCreated++;

            var blDate = Today.AddDays(-2);
            var ship = await CreateShipmentAsync(lk, po, NextBl("NW"), blDate, blDate.AddDays(30), ShipmentStatus.Draft, marineInsurance: false);
            await AddShipmentLineItemsAsync(ship, lines);
            shipmentsCreated++;
        }

        // --- 10 In-transit shipments at varied document stages ---
        var transitStages = new[] { "draft-only", "draft-only", "full-set", "full-set", "banking", "banking", "full-set", "banking", "draft-only", "full-set" };
        for (int i = 0; i < transitStages.Length; i++)
        {
            var fam = FamAt(i + 1);
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 12 + i, OrderStatus.Confirmed);
            var lines = await AddPoLineItemsAsync(lk, po, 1);
            posCreated++;

            var blDate = Today.AddDays(-(8 + i));
            var ship = await CreateShipmentAsync(lk, po, NextBl("IT"), blDate, blDate.AddDays(25), ShipmentStatus.Confirmed, marineInsurance: i % 2 == 0);
            await AddShipmentLineItemsAsync(ship, lines);
            await AddDraftDocsAsync(ship, blDate);
            if (transitStages[i] != "draft-only") await AddSupplierFullSetAsync(ship, blDate);
            if (transitStages[i] == "banking") await AddBankingAsync(ship, blDate);
            await AddForwarderAsync(ship, lk, marineInsurance: i % 2 == 0);
            // COC/SSMO: 1 in 3 required-but-not-done, 1 in 3 not needed, rest done
            if (i % 3 == 0) await AddMotAsync(ship, blDate, approved: false);
            else if (i % 3 == 1) { /* not required — skip entirely */ }
            else await AddMotAsync(ship, blDate, approved: true);
            shipmentsCreated++;
        }

        // Helper to arrive + start clearance on one of the fully-executed shipments.
        async Task<Clearance> ArriveAndClear(Shipment ship, int arrivedDaysAgo, ClearanceRouteType route)
        {
            var arrival = Today.AddDays(-arrivedDaysAgo);
            var clearance = await CreateClearanceAsync(ship, route, arrival);
            await AddDeliveryOrderAsync(clearance, arrival, completed: true);
            await AddCertificateEntryAsync(clearance, arrival);
            return clearance;
        }

        // --- Route 1 shipments: 5 on-track, 4 behind schedule, 3 stuck on SSMO, 3 with demurrage exposure ---
        // (15 shipments total: the 7 fully-executed ones are reused, plus 8 fresh ones)
        var r1Scenarios = new (int arrivedDaysAgo, int stepsDone, decimal paidFraction, string label)[]
        {
            (5, 4, 1.0m, "on-track"), (7, 5, 0.8m, "on-track"), (4, 3, 1.0m, "on-track"), (9, 6, 0.5m, "on-track"), (3, 2, 1.0m, "on-track"),
            (18, 3, 0.3m, "behind"), (22, 4, 0.2m, "behind"), (16, 2, 0.4m, "behind"), (20, 5, 0.1m, "behind"),
            (12, 2, 0.0m, "stuck-ssmo"), (15, 1, 0.0m, "stuck-ssmo"), (10, 2, 0.0m, "stuck-ssmo"),
            (28, 6, 0.9m, "demurrage"), (25, 5, 0.7m, "demurrage"), (30, 7, 1.0m, "demurrage"),
        };
        for (int i = 0; i < r1Scenarios.Count(); i++)
        {
            var (arrivedDaysAgo, stepsDone, paidFraction, label) = r1Scenarios[i];
            Shipment ship;
            if (i < fullyExecutedShipments.Count)
            {
                ship = fullyExecutedShipments[i];
            }
            else
            {
                var fam = FamAt(i);
                var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: arrivedDaysAgo + 30, OrderStatus.Confirmed);
                var lines = await AddPoLineItemsAsync(lk, po, 1);
                posCreated++;
                var blDate = Today.AddDays(-(arrivedDaysAgo + 20));
                ship = await CreateShipmentAsync(lk, po, NextBl("R1"), blDate, Today.AddDays(-arrivedDaysAgo), ShipmentStatus.Confirmed, marineInsurance: true);
                await AddShipmentLineItemsAsync(ship, lines);
                await AddDraftDocsAsync(ship, blDate);
                await AddSupplierFullSetAsync(ship, blDate);
                await AddBankingAsync(ship, blDate);
                await AddForwarderAsync(ship, lk, marineInsurance: true);
                await AddAcdAsync(ship, blDate);
                await AddMotAsync(ship, blDate, approved: label != "stuck-ssmo");
                shipmentsCreated++;
            }

            var clearance = await ArriveAndClear(ship, arrivedDaysAgo, ClearanceRouteType.Route1ClearAtPort);
            await AddCostEstimateAsync(clearance, lk, Today.AddDays(-arrivedDaysAgo), paidFraction);
            await AddRoute1ProgressAsync(clearance, Today.AddDays(-arrivedDaysAgo + 1), stepsDone);

            // Trucks for shipments nearing/at completion.
            if (stepsDone >= 6)
            {
                var shipLines = await _db.ShipmentLineItems.Where(sl => sl.ShipmentId == ship.Id).ToListAsync();
                if (shipLines.Count > 0)
                {
                    var truckStatus = label == "on-track" ? "OnTime" : label == "demurrage" ? "Delayed" : "Arrived";
                    await AddTruckAllocationAsync(lk, shipLines[0], Today.AddDays(-arrivedDaysAgo + stepsDone), truckStatus);
                }
            }
        }

        // --- 4 Route 2 shipments — fully deposited in FZ (real FZ inventory) ---
        var fzFamilies = new[] { Families[2], Families[3], Families[2], Families[3] }; // Delta + Digitech families (they have FZ partners in their chain)
        var fzDestinations = new[] { lk.Psdfz, lk.GarriFz, lk.GarriFz, lk.Psdfz };
        for (int i = 0; i < 4; i++)
        {
            var fam = fzFamilies[i];
            var po = await CreatePoAsync(lk, fam, NextPo(), placedDaysAgo: 30 + i * 3, OrderStatus.Confirmed);
            var lines = await AddPoLineItemsAsync(lk, po, 1);
            posCreated++;

            var arrivedDaysAgo = 10 + i * 2;
            var blDate = Today.AddDays(-(arrivedDaysAgo + 15));
            var ship = await CreateShipmentAsync(lk, po, NextBl("FZ"), blDate, Today.AddDays(-arrivedDaysAgo), ShipmentStatus.Confirmed, marineInsurance: true);
            await AddShipmentLineItemsAsync(ship, lines);
            await AddDraftDocsAsync(ship, blDate);
            await AddSupplierFullSetAsync(ship, blDate);
            await AddBankingAsync(ship, blDate);
            await AddForwarderAsync(ship, lk, marineInsurance: true);
            await AddMotAsync(ship, blDate, approved: true);
            shipmentsCreated++;

            var clearance = await ArriveAndClear(ship, arrivedDaysAgo, ClearanceRouteType.Route2FzDeposit);
            await AddCostEstimateAsync(clearance, lk, Today.AddDays(-arrivedDaysAgo), 1.0m);
            await AddRoute2ProgressAsync(clearance, fzDestinations[i], Today.AddDays(-arrivedDaysAgo + 1), depositedAtFz: true);
        }

            return $"Generated {poCounter - 1} Purchase Orders and {blCounter - 1} Shipments, with varied document/clearance/FZ/trucking stages across all 7 real BU/Division/Supplier families.";
    }
}
