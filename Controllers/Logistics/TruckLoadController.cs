using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;

namespace ShippingPortal.Api.Controllers.Logistics;

public record CreateTruckLoadRequest(int TruckId, int? DriverId, DateOnly LoadDate, string? Notes);
public record TruckLoadSummary(int Id, string PlateNo, string? DriverName, DateOnly LoadDate, int DropCount, int ItemCount);

public record TruckLoadItemRow(
    int TruckLoadItemId, int TruckLoadId, string PlateNo, string? DriverName, DateOnly LoadDate,
    string WarehouseName, string? City, DateOnly? ExpectedDeliveryDate, DateOnly? ActualDropOffDate,
    string ModelProduct, string Unit, decimal Qty, decimal? InHousePrice, decimal? ParallelMarketPrice, bool IsCompleted);

public record AddDropRequest(int WarehouseId, DateOnly? ExpectedDeliveryDate);
public record SetActualDropOffRequest(DateOnly? ActualDropOffDate);
public record DropSummary(int Id, int WarehouseId, string WarehouseName, string? City, DateOnly? ExpectedDeliveryDate, DateOnly? ActualDropOffDate);

public record AddTruckLoadItemRequest(int WarehouseAllocationId, decimal Qty, decimal? InHousePrice, decimal? ParallelMarketPrice);
public record TruckLoadItemSummary(
    int Id, int WarehouseAllocationId, string ModelProduct, string Unit, decimal Qty,
    decimal? InHousePrice, decimal? ParallelMarketPrice);

public record TruckLoadDetailResponse(
    int Id, int TruckId, string PlateNo, int? DriverId, string? DriverName, DateOnly LoadDate, string? Notes,
    List<TruckLoadDropDetail> Drops);
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
    string WarehouseName, decimal AllocatedQty, decimal LoadedQty, decimal RemainingQty, DateTime AllocatedAt);

[ApiController]
[Authorize(Roles = AppRoles.LogisticsViewers)]
[Route("api/truck-loads")]
public class TruckLoadController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public TruckLoadController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TruckLoadSummary>>> GetAll()
    {
        var loads = await _db.TruckLoads
            .Include(t => t.Truck).Include(t => t.Driver)
            .OrderByDescending(t => t.LoadDate)
            .ToListAsync();

        var result = new List<TruckLoadSummary>();
        foreach (var load in loads)
        {
            var dropCount = await _db.TruckLoadDrops.CountAsync(d => d.TruckLoadId == load.Id);
            var itemCount = await _db.TruckLoadItems.CountAsync(i => i.TruckLoadDrop!.TruckLoadId == load.Id);
            result.Add(new TruckLoadSummary(load.Id, load.Truck!.PlateNo, load.Driver?.Name, load.LoadDate, dropCount, itemCount));
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
                i.Id, load.Id, load.Truck!.PlateNo, load.Driver?.Name, load.LoadDate,
                drop.Warehouse!.Name, drop.Warehouse.City?.Name, drop.ExpectedDeliveryDate, drop.ActualDropOffDate,
                portLi?.ModelProduct?.Name ?? withdrawalLi?.ModelProduct?.Name ?? "",
                portLi?.UnitOfMeasure?.Code ?? withdrawalLi?.UnitOfMeasure?.Code ?? "",
                i.Qty, i.InHousePrice, i.ParallelMarketPrice, drop.ActualDropOffDate.HasValue);
        }).ToList();

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<ActionResult<TruckLoadSummary>> Create(CreateTruckLoadRequest req)
    {
        var truck = await _db.Trucks.FindAsync(req.TruckId);
        if (truck is null) return NotFound(new { message = "Truck not found." });

        var load = new TruckLoad { TruckId = req.TruckId, DriverId = req.DriverId, LoadDate = req.LoadDate, Notes = req.Notes };
        _db.TruckLoads.Add(load);
        await _db.SaveChangesAsync();

        var driver = req.DriverId.HasValue ? await _db.Drivers.FindAsync(req.DriverId.Value) : null;
        return Ok(new TruckLoadSummary(load.Id, truck.PlateNo, driver?.Name, load.LoadDate, 0, 0));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TruckLoadDetailResponse>> GetDetail(int id)
    {
        var load = await _db.TruckLoads.Include(t => t.Truck).Include(t => t.Driver).FirstOrDefaultAsync(t => t.Id == id);
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

        return Ok(new TruckLoadDetailResponse(load.Id, load.TruckId, load.Truck!.PlateNo, load.DriverId, load.Driver?.Name, load.LoadDate, load.Notes, dropDetails));
    }

    [HttpPost("{id:int}/drops")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> AddDrop(int id, AddDropRequest req)
    {
        if (!await _db.TruckLoads.AnyAsync(t => t.Id == id)) return NotFound();

        var drop = new TruckLoadDrop { TruckLoadId = id, WarehouseId = req.WarehouseId, ExpectedDeliveryDate = req.ExpectedDeliveryDate };
        _db.TruckLoadDrops.Add(drop);
        await _db.SaveChangesAsync();
        return Ok(new { id = drop.Id });
    }

    [HttpPut("drops/{dropId:int}/actual-dropoff")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> SetActualDropOff(int dropId, SetActualDropOffRequest req)
    {
        var drop = await _db.TruckLoadDrops.FindAsync(dropId);
        if (drop is null) return NotFound();

        drop.ActualDropOffDate = req.ActualDropOffDate;
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
            .Include(a => a.Warehouse)
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
                a.Warehouse!.Name, a.Qty, loaded, remaining, a.AllocatedAt));
        }
        return Ok(result.OrderBy(r => r.AllocatedAt).ToList());
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
