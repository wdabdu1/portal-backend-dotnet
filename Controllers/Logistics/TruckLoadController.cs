using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;

namespace ShippingPortal.Api.Controllers.Logistics;

// Internal: TruckId (+ optional DriverId) from the in-house fleet. External:
// IsExternal = true, ExternalTransporterId + ExternalTruckNo + ExternalDriverName
// required (phone optional) - typed in per trip, never a Settings truck/driver.
public record CreateTruckLoadRequest(
    int? TruckId, int? DriverId, DateOnly LoadDate, string? Notes,
    bool IsExternal = false, int? ExternalTransporterId = null, string? ExternalTruckNo = null,
    string? ExternalDriverName = null, string? ExternalDriverPhone = null);
public record TruckLoadSummary(
    int Id, string PlateNo, string? DriverName, DateOnly LoadDate, int DropCount, int ItemCount,
    bool IsExternal = false, string? TransporterName = null, string? DriverPhone = null);

public record TruckLoadItemRow(
    int TruckLoadItemId, int TruckLoadId, string PlateNo, string? DriverName, DateOnly LoadDate,
    string WarehouseName, string? City, DateOnly? ExpectedDeliveryDate, DateOnly? ActualDropOffDate,
    string ModelProduct, string Unit, decimal Qty, decimal? InHousePrice, decimal? ParallelMarketPrice, bool IsCompleted,
    bool IsExternal = false, string? TransporterName = null, string? DriverPhone = null);

public record AddDropRequest(int WarehouseId, DateOnly? ExpectedDeliveryDate);
public record SetActualDropOffRequest(DateOnly? ActualDropOffDate);
public record SetExpectedDeliveryRequest(DateOnly? ExpectedDeliveryDate);
public record DropSummary(int Id, int WarehouseId, string WarehouseName, string? City, DateOnly? ExpectedDeliveryDate, DateOnly? ActualDropOffDate);

public record AddTruckLoadItemRequest(int WarehouseAllocationId, decimal Qty, decimal? InHousePrice, decimal? ParallelMarketPrice);
public record TruckLoadItemSummary(
    int Id, int WarehouseAllocationId, string ModelProduct, string Unit, decimal Qty,
    decimal? InHousePrice, decimal? ParallelMarketPrice);

public record TruckLoadDetailResponse(
    int Id, int? TruckId, string PlateNo, int? DriverId, string? DriverName, DateOnly LoadDate, string? Notes,
    List<TruckLoadDropDetail> Drops,
    bool IsExternal = false, int? ExternalTransporterId = null, string? TransporterName = null, string? DriverPhone = null);
public record TruckLoadDropDetail(int Id, int WarehouseId, string WarehouseName, string? City, DateOnly? ExpectedDeliveryDate, DateOnly? ActualDropOffDate, List<TruckLoadItemSummary> Items);

// Which WarehouseAllocations still have quantity not yet assigned to any
// truck — the picker source when adding items to a drop. Filtered to the
// drop's own warehouse, since an allocation can only be loaded for the
// warehouse it was actually allocated to.
public record LoadableAllocation(int WarehouseAllocationId, string ModelProduct, string Unit, string BlAwbNo, decimal RemainingToLoad);

// Every warehouse allocation still waiting for a truck — the queue that
// closes the gap between "item allocated to a warehouse" and "someone
// notices it needs a truck."
public record ReadyForTruckAssignment(
    int WarehouseAllocationId, string BusinessUnit, string ModelProduct, string Unit, string BlAwbNo,
    int WarehouseId, string WarehouseName, string? City, decimal AllocatedQty, decimal LoadedQty, decimal RemainingQty, DateTime AllocatedAt);

public record QuickAssignItem(int WarehouseAllocationId, decimal Qty, decimal? InHousePrice, decimal? ParallelMarketPrice);
public record QuickAssignRequest(
    int? TruckId, int? DriverId, DateOnly LoadDate, int WarehouseId, DateOnly? ExpectedDeliveryDate, List<QuickAssignItem> Items,
    bool IsExternal = false, int? ExternalTransporterId = null, string? ExternalTruckNo = null,
    string? ExternalDriverName = null, string? ExternalDriverPhone = null);

[ApiController]
[Authorize(Roles = AppRoles.LogisticsViewers)]
[Route("api/truck-loads")]
public class TruckLoadController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public TruckLoadController(ShippingPortalDbContext db) => _db = db;

    // Returns an error message, or null when the chosen mode has everything it needs.
    private async Task<string?> ValidateModeAsync(
        bool isExternal, int? truckId, int? externalTransporterId, string? externalTruckNo, string? externalDriverName)
    {
        if (isExternal)
        {
            if (!externalTransporterId.HasValue || !await _db.ExternalTransporters.AnyAsync(t => t.Id == externalTransporterId.Value))
                return "Choose the external transporter.";
            if (string.IsNullOrWhiteSpace(externalTruckNo)) return "Enter the external truck number.";
            if (string.IsNullOrWhiteSpace(externalDriverName)) return "Enter the external driver's name.";
            return null;
        }
        if (!truckId.HasValue || !await _db.Trucks.AnyAsync(t => t.Id == truckId.Value))
            return "Truck not found.";
        return null;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TruckLoadSummary>>> GetAll()
    {
        var loads = await _db.TruckLoads
            .Include(t => t.Truck).Include(t => t.Driver).Include(t => t.ExternalTransporter)
            .OrderByDescending(t => t.LoadDate)
            .ToListAsync();

        var result = new List<TruckLoadSummary>();
        foreach (var load in loads)
        {
            var dropCount = await _db.TruckLoadDrops.CountAsync(d => d.TruckLoadId == load.Id);
            var itemCount = await _db.TruckLoadItems.CountAsync(i => i.TruckLoadDrop!.TruckLoadId == load.Id);
            result.Add(new TruckLoadSummary(load.Id, load.TruckDisplay, load.DriverDisplay, load.LoadDate, dropCount, itemCount,
                load.IsExternal, load.ExternalTransporter?.Name, load.DriverPhoneDisplay));
        }
        return Ok(result);
    }

    // Item-level flat view: one row per TruckLoadItem, so the same truck
    // and drop naturally repeat across rows for multi-drop trips.
    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<TruckLoadItemRow>>> GetItems()
    {
        var items = await _db.TruckLoadItems
            .Include(i => i.TruckLoadDrop).ThenInclude(d => d!.TruckLoad).ThenInclude(t => t!.Truck)
            .Include(i => i.TruckLoadDrop).ThenInclude(d => d!.TruckLoad).ThenInclude(t => t!.Driver)
            .Include(i => i.TruckLoadDrop).ThenInclude(d => d!.TruckLoad).ThenInclude(t => t!.ExternalTransporter)
            .Include(i => i.TruckLoadDrop).ThenInclude(d => d!.Warehouse).ThenInclude(w => w!.City)
            .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .ToListAsync();

        var result = items.Select(i =>
        {
            var drop = i.TruckLoadDrop!;
            var load = drop.TruckLoad!;
            var portLi = i.WarehouseAllocation?.ShipmentLineItem?.PurchaseOrderLineItem;
            var withdrawalLi = i.WarehouseAllocation?.WithdrawalLineItem?.DepositShipmentLineItem?.PurchaseOrderLineItem;

            return new TruckLoadItemRow(
                i.Id, load.Id, load.TruckDisplay, load.DriverDisplay, load.LoadDate,
                drop.Warehouse!.Name, drop.Warehouse.City?.Name, drop.ExpectedDeliveryDate, drop.ActualDropOffDate,
                portLi?.ModelProduct?.Name ?? withdrawalLi?.ModelProduct?.Name ?? "",
                portLi?.UnitOfMeasure?.Code ?? withdrawalLi?.UnitOfMeasure?.Code ?? "",
                i.Qty, i.InHousePrice, i.ParallelMarketPrice, drop.ActualDropOffDate.HasValue,
                load.IsExternal, load.ExternalTransporter?.Name, load.DriverPhoneDisplay);
        }).ToList();

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<ActionResult<TruckLoadSummary>> Create(CreateTruckLoadRequest req)
    {
        var modeError = await ValidateModeAsync(req.IsExternal, req.TruckId, req.ExternalTransporterId, req.ExternalTruckNo, req.ExternalDriverName);
        if (modeError is not null) return BadRequest(new { message = modeError });

        var load = new TruckLoad { LoadDate = req.LoadDate, Notes = req.Notes, IsExternal = req.IsExternal };
        if (req.IsExternal)
        {
            load.ExternalTransporterId = req.ExternalTransporterId;
            load.ExternalTruckNo = Clean(req.ExternalTruckNo);
            load.ExternalDriverName = Clean(req.ExternalDriverName);
            load.ExternalDriverPhone = Clean(req.ExternalDriverPhone);
        }
        else
        {
            load.TruckId = req.TruckId;
            load.DriverId = req.DriverId;
        }
        _db.TruckLoads.Add(load);
        await _db.SaveChangesAsync();

        var saved = await _db.TruckLoads.Include(t => t.Truck).Include(t => t.Driver).Include(t => t.ExternalTransporter).FirstAsync(t => t.Id == load.Id);
        return Ok(new TruckLoadSummary(saved.Id, saved.TruckDisplay, saved.DriverDisplay, saved.LoadDate, 0, 0,
            saved.IsExternal, saved.ExternalTransporter?.Name, saved.DriverPhoneDisplay));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TruckLoadDetailResponse>> GetDetail(int id)
    {
        var load = await _db.TruckLoads.Include(t => t.Truck).Include(t => t.Driver).Include(t => t.ExternalTransporter).FirstOrDefaultAsync(t => t.Id == id);
        if (load is null) return NotFound();

        var drops = await _db.TruckLoadDrops.Where(d => d.TruckLoadId == id).Include(d => d.Warehouse).ThenInclude(w => w!.City).ToListAsync();
        var dropDetails = new List<TruckLoadDropDetail>();

        foreach (var drop in drops)
        {
            var items = await _db.TruckLoadItems
                .Where(i => i.TruckLoadDropId == drop.Id)
                .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
                .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
                .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
                .Include(i => i.WarehouseAllocation).ThenInclude(a => a!.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
                .ToListAsync();

            var itemSummaries = items.Select(i =>
            {
                var portLi = i.WarehouseAllocation?.ShipmentLineItem?.PurchaseOrderLineItem;
                var withdrawalLi = i.WarehouseAllocation?.WithdrawalLineItem?.DepositShipmentLineItem?.PurchaseOrderLineItem;
                var product = portLi?.ModelProduct?.Name ?? withdrawalLi?.ModelProduct?.Name ?? "";
                var unit = portLi?.UnitOfMeasure?.Code ?? withdrawalLi?.UnitOfMeasure?.Code ?? "";
                return new TruckLoadItemSummary(i.Id, i.WarehouseAllocationId, product, unit, i.Qty, i.InHousePrice, i.ParallelMarketPrice);
            }).ToList();

            dropDetails.Add(new TruckLoadDropDetail(drop.Id, drop.WarehouseId, drop.Warehouse!.Name, drop.Warehouse.City?.Name, drop.ExpectedDeliveryDate, drop.ActualDropOffDate, itemSummaries));
        }

        return Ok(new TruckLoadDetailResponse(load.Id, load.TruckId, load.TruckDisplay, load.DriverId, load.DriverDisplay, load.LoadDate, load.Notes, dropDetails,
            load.IsExternal, load.ExternalTransporterId, load.ExternalTransporter?.Name, load.DriverPhoneDisplay));
    }

    [HttpPost("{id:int}/drops")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> AddDrop(int id, AddDropRequest req)
    {
        var loadDate = await _db.TruckLoads.Where(t => t.Id == id).Select(t => (DateOnly?)t.LoadDate).FirstOrDefaultAsync();
        if (!loadDate.HasValue) return NotFound();

        // No expected date typed: use the destination city's delivery target, if one is set.
        var expectedDate = req.ExpectedDeliveryDate;
        if (!expectedDate.HasValue)
        {
            var cityTarget = await _db.Warehouses
                .Where(w => w.Id == req.WarehouseId)
                .Select(w => w.City != null ? w.City.TargetDeliveryDays : (int?)null)
                .FirstOrDefaultAsync();
            if (cityTarget.HasValue) expectedDate = loadDate.Value.AddDays(cityTarget.Value);
        }

        var drop = new TruckLoadDrop { TruckLoadId = id, WarehouseId = req.WarehouseId, ExpectedDeliveryDate = expectedDate };
        _db.TruckLoadDrops.Add(drop);
        await _db.SaveChangesAsync();
        return Ok(new { id = drop.Id });
    }

    [HttpPut("drops/{dropId:int}/actual-dropoff")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> SetActualDropOff(int dropId, SetActualDropOffRequest req)
    {
        var drop = await _db.TruckLoadDrops.Include(d => d.Warehouse).FirstOrDefaultAsync(d => d.Id == dropId);
        if (drop is null) return NotFound();

        drop.ActualDropOffDate = req.ActualDropOffDate;

        // Completing a drop only moves the truck if it's the LAST drop in
        // this trip (drops are visited in the order they were added) —
        // an earlier stop finishing doesn't mean the truck is done moving.
        if (req.ActualDropOffDate.HasValue && drop.Warehouse?.CityId is not null)
        {
            var lastDropId = await _db.TruckLoadDrops.Where(d => d.TruckLoadId == drop.TruckLoadId).MaxAsync(d => (int?)d.Id);
            if (lastDropId == dropId)
            {
                var truckLoad = await _db.TruckLoads.FindAsync(drop.TruckLoadId);
                // External trucks are not in the fleet list, so there is no position to update.
                if (truckLoad is not null && truckLoad.TruckId.HasValue)
                {
                    var truck = await _db.Trucks.FindAsync(truckLoad.TruckId.Value);
                    if (truck is not null) truck.CurrentCityId = drop.Warehouse.CityId;
                }
            }
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Updates the planned date only — separate from actual-dropoff below,
    // since a slipping ETA and a confirmed arrival are different events and
    // a truck can need its ETA pushed back several times before it ever
    // arrives. Does not touch the truck's CurrentCityId; only completing
    // the drop (actual-dropoff) does that.
    [HttpPut("drops/{dropId:int}/expected-delivery")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> SetExpectedDelivery(int dropId, SetExpectedDeliveryRequest req)
    {
        var drop = await _db.TruckLoadDrops.FindAsync(dropId);
        if (drop is null) return NotFound();

        drop.ExpectedDeliveryDate = req.ExpectedDeliveryDate;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("drops/{dropId:int}")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> DeleteDrop(int dropId)
    {
        var drop = await _db.TruckLoadDrops.FindAsync(dropId);
        if (drop is null) return NotFound();

        _db.TruckLoadDrops.Remove(drop);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("ready-for-assignment")]
    public async Task<ActionResult<IEnumerable<ReadyForTruckAssignment>>> GetReadyForAssignment()
    {
        var allocations = await _db.WarehouseAllocations
            .Include(a => a.Warehouse!).ThenInclude(w => w.City)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .ToListAsync();

        var loadedByAllocation = await _db.TruckLoadItems
            .GroupBy(i => i.WarehouseAllocationId)
            .Select(g => new { AllocationId = g.Key, Total = g.Sum(i => i.Qty) })
            .ToDictionaryAsync(x => x.AllocationId, x => x.Total);

        var result = new List<ReadyForTruckAssignment>();
        foreach (var a in allocations)
        {
            var loaded = loadedByAllocation.GetValueOrDefault(a.Id);
            var remaining = a.Qty - loaded;
            if (remaining <= 0) continue;

            var portLi = a.ShipmentLineItem?.PurchaseOrderLineItem;
            var withdrawalLi = a.WithdrawalLineItem?.DepositShipmentLineItem?.PurchaseOrderLineItem;
            var blAwbNo = a.ShipmentLineItem?.Shipment?.BlAwbNo ?? a.WithdrawalLineItem?.DepositShipmentLineItem?.Shipment?.BlAwbNo ?? "";
            var businessUnit = a.ShipmentLineItem?.Shipment?.PurchaseOrder?.BusinessUnit?.Name ?? a.WithdrawalLineItem?.DepositShipmentLineItem?.Shipment?.PurchaseOrder?.BusinessUnit?.Name ?? "";

            result.Add(new ReadyForTruckAssignment(
                a.Id, businessUnit, portLi?.ModelProduct?.Name ?? withdrawalLi?.ModelProduct?.Name ?? "",
                portLi?.UnitOfMeasure?.Code ?? withdrawalLi?.UnitOfMeasure?.Code ?? "", blAwbNo,
                a.WarehouseId, a.Warehouse!.Name, a.Warehouse.City?.Name, a.Qty, loaded, remaining, a.AllocatedAt));
        }
        return Ok(result.OrderBy(r => r.AllocatedAt).ToList());
    }

    // Combines Create Load + Add Drop + Add Items into one atomic action —
    // this is the whole point of the redesign: no more navigating away to
    // a separate, generic Truck Loads screen and re-finding the item.
    // Reuses an existing load for the same truck+date, and an existing
    // drop for the same warehouse within that load, so assigning several
    // cities' items to one truck on one day doesn't create duplicates.
    [HttpPost("quick-assign")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> QuickAssign(QuickAssignRequest req)
    {
        if (req.Items.Count == 0) return BadRequest(new { message = "At least one item is required." });
        var modeError = await ValidateModeAsync(req.IsExternal, req.TruckId, req.ExternalTransporterId, req.ExternalTruckNo, req.ExternalDriverName);
        if (modeError is not null) return BadRequest(new { message = modeError });

        // Reuse the same trip when it's the same vehicle on the same day: same fleet truck
        // (internal) or same transporter + truck number (external) - never mixed.
        TruckLoad? load;
        if (req.IsExternal)
        {
            var truckNo = Clean(req.ExternalTruckNo)!;
            load = await _db.TruckLoads.FirstOrDefaultAsync(l => l.IsExternal && l.ExternalTransporterId == req.ExternalTransporterId
                && l.ExternalTruckNo == truckNo && l.LoadDate == req.LoadDate);
        }
        else
        {
            load = await _db.TruckLoads.FirstOrDefaultAsync(l => !l.IsExternal && l.TruckId == req.TruckId && l.LoadDate == req.LoadDate);
        }
        if (load is null)
        {
            load = new TruckLoad { LoadDate = req.LoadDate, IsExternal = req.IsExternal };
            if (req.IsExternal)
            {
                load.ExternalTransporterId = req.ExternalTransporterId;
                load.ExternalTruckNo = Clean(req.ExternalTruckNo);
                load.ExternalDriverName = Clean(req.ExternalDriverName);
                load.ExternalDriverPhone = Clean(req.ExternalDriverPhone);
            }
            else
            {
                load.TruckId = req.TruckId;
                load.DriverId = req.DriverId;
            }
            _db.TruckLoads.Add(load);
            await _db.SaveChangesAsync();
        }

        var drop = await _db.TruckLoadDrops.FirstOrDefaultAsync(d => d.TruckLoadId == load.Id && d.WarehouseId == req.WarehouseId);
        if (drop is null)
        {
            // No expected date typed: use the destination city's delivery target (calendar days from load date), if one is set.
            var expectedDate = req.ExpectedDeliveryDate;
            if (!expectedDate.HasValue)
            {
                var cityTarget = await _db.Warehouses
                    .Where(w => w.Id == req.WarehouseId)
                    .Select(w => w.City != null ? w.City.TargetDeliveryDays : (int?)null)
                    .FirstOrDefaultAsync();
                if (cityTarget.HasValue) expectedDate = req.LoadDate.AddDays(cityTarget.Value);
            }
            drop = new TruckLoadDrop { TruckLoadId = load.Id, WarehouseId = req.WarehouseId, ExpectedDeliveryDate = expectedDate };
            _db.TruckLoadDrops.Add(drop);
            await _db.SaveChangesAsync();
        }

        foreach (var item in req.Items)
        {
            if (item.Qty <= 0) return BadRequest(new { message = "Quantity must be greater than zero." });

            var allocation = await _db.WarehouseAllocations.FindAsync(item.WarehouseAllocationId);
            if (allocation is null) return BadRequest(new { message = $"Allocation {item.WarehouseAllocationId} not found." });

            var alreadyLoaded = await _db.TruckLoadItems
                .Where(i => i.WarehouseAllocationId == item.WarehouseAllocationId)
                .SumAsync(i => (decimal?)i.Qty) ?? 0;
            if (item.Qty > allocation.Qty - alreadyLoaded)
            {
                return BadRequest(new { message = $"Requested quantity ({item.Qty}) exceeds this allocation's remaining unloaded quantity ({allocation.Qty - alreadyLoaded})." });
            }

            _db.TruckLoadItems.Add(new TruckLoadItem
            {
                TruckLoadDropId = drop.Id, WarehouseAllocationId = item.WarehouseAllocationId, Qty = item.Qty,
                InHousePrice = item.InHousePrice, ParallelMarketPrice = item.ParallelMarketPrice
            });
        }
        await _db.SaveChangesAsync();

        return Ok(new { truckLoadId = load.Id, dropId = drop.Id });
    }

    [HttpGet("drops/{dropId:int}/loadable-allocations")]
    public async Task<ActionResult<IEnumerable<LoadableAllocation>>> GetLoadableAllocations(int dropId)
    {
        var drop = await _db.TruckLoadDrops.FindAsync(dropId);
        if (drop is null) return NotFound();

        var allocations = await _db.WarehouseAllocations
            .Where(a => a.WarehouseId == drop.WarehouseId)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .Include(a => a.ShipmentLineItem).ThenInclude(li => li!.Shipment)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .Include(a => a.WithdrawalLineItem).ThenInclude(li => li!.DepositShipmentLineItem).ThenInclude(li => li!.Shipment)
            .ToListAsync();

        var loadedByAllocation = await _db.TruckLoadItems
            .GroupBy(i => i.WarehouseAllocationId)
            .Select(g => new { AllocationId = g.Key, Total = g.Sum(i => i.Qty) })
            .ToDictionaryAsync(x => x.AllocationId, x => x.Total);

        var result = new List<LoadableAllocation>();
        foreach (var a in allocations)
        {
            var loaded = loadedByAllocation.GetValueOrDefault(a.Id);
            var remaining = a.Qty - loaded;
            if (remaining <= 0) continue;

            var portLi = a.ShipmentLineItem?.PurchaseOrderLineItem;
            var withdrawalLi = a.WithdrawalLineItem?.DepositShipmentLineItem?.PurchaseOrderLineItem;
            var blAwbNo = a.ShipmentLineItem?.Shipment?.BlAwbNo ?? a.WithdrawalLineItem?.DepositShipmentLineItem?.Shipment?.BlAwbNo ?? "";

            result.Add(new LoadableAllocation(
                a.Id, portLi?.ModelProduct?.Name ?? withdrawalLi?.ModelProduct?.Name ?? "",
                portLi?.UnitOfMeasure?.Code ?? withdrawalLi?.UnitOfMeasure?.Code ?? "", blAwbNo, remaining));
        }
        return Ok(result);
    }

    [HttpPost("drops/{dropId:int}/items")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> AddItem(int dropId, AddTruckLoadItemRequest req)
    {
        if (req.Qty <= 0) return BadRequest(new { message = "Quantity must be greater than zero." });
        if (!await _db.TruckLoadDrops.AnyAsync(d => d.Id == dropId)) return NotFound();

        var allocation = await _db.WarehouseAllocations.FindAsync(req.WarehouseAllocationId);
        if (allocation is null) return NotFound(new { message = "Allocation not found." });

        var alreadyLoaded = await _db.TruckLoadItems
            .Where(i => i.WarehouseAllocationId == req.WarehouseAllocationId)
            .SumAsync(i => (decimal?)i.Qty) ?? 0;

        if (req.Qty > allocation.Qty - alreadyLoaded)
        {
            return BadRequest(new { message = $"Requested quantity ({req.Qty}) exceeds this allocation's remaining unloaded quantity ({allocation.Qty - alreadyLoaded})." });
        }

        var item = new TruckLoadItem
        {
            TruckLoadDropId = dropId,
            WarehouseAllocationId = req.WarehouseAllocationId,
            Qty = req.Qty,
            InHousePrice = req.InHousePrice,
            ParallelMarketPrice = req.ParallelMarketPrice
        };
        _db.TruckLoadItems.Add(item);
        await _db.SaveChangesAsync();
        return Ok(new { id = item.Id });
    }

    [HttpDelete("items/{itemId:int}")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> DeleteItem(int itemId)
    {
        var item = await _db.TruckLoadItems.FindAsync(itemId);
        if (item is null) return NotFound();

        _db.TruckLoadItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
