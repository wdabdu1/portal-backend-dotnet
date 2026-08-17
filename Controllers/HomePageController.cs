using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

public record HomePoRow(string BusinessUnit, string PoNumber, string Supplier, DateOnly TargetDate);
public record HomeShipmentRow(string BusinessUnit, string Consignee, string Category, string BlAwbNo, string Extra1, DateOnly TargetDate, bool IsActual);
public record HomeTruckRow(string BusinessUnit, string Consignee, string Category, string TruckNo, string Extra1, DateOnly TargetDate, bool IsActual);

public record HomePageResponse(
    bool ShowPos, List<HomePoRow> RecentPos,
    bool ShowShipments, List<HomeShipmentRow> RecentShipments,
    bool ShowArrivals, List<HomeShipmentRow> ArrivedArrivingShipments,
    bool ShowClearance, List<HomeShipmentRow> ClearedAboutToClear,
    bool ShowFz, List<HomeShipmentRow> RecentlyDeposited, List<HomeShipmentRow> WithdrawnAboutToWithdraw,
    bool ShowTrucks, List<HomeTruckRow> AllocatedTrucks, List<HomeTruckRow> ArrivedArrivingTrucks,
    int RedAlertCount);

// A section only appears at all if the user's role permits it — this
// is deliberately role-gated, not just BU-filtered, per how access is
// meant to work here: e.g. a Clearance-only user never sees PO/Supplier
// sections, regardless of which BUs they're otherwise scoped to.
[ApiController]
[Route("api/dashboards/home")]
[Authorize]
public class HomePageController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public HomePageController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<HomePageResponse>> Get([FromServices] BuAccessService buAccess)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = today.AddDays(-2);
        var windowEnd = today.AddDays(2);

        var seesAllBus = buAccess.SeesAllBus(User);
        var allowedBuIds = seesAllBus ? null : buAccess.GetAllowedBusinessUnitIds(User);

        bool InAnyRole(params string[] roles) => roles.Any(User.IsInRole);

        var showPos = InAnyRole(AppRoles.IpUser, AppRoles.IpSupervisor, AppRoles.Bu, AppRoles.Treasury, AppRoles.Manager, AppRoles.SuperUser);
        var showShipments = showPos;
        var showArrivals = InAnyRole(AppRoles.IpUser, AppRoles.IpSupervisor, AppRoles.ClrUsr, AppRoles.ClrSupervisor, AppRoles.Bu, AppRoles.Treasury, AppRoles.Manager, AppRoles.SuperUser);
        var showClearance = InAnyRole(AppRoles.IpUser, AppRoles.IpSupervisor, AppRoles.ClrUsr, AppRoles.ClrSupervisor, AppRoles.Bu, AppRoles.Treasury, AppRoles.CorpFinance, AppRoles.Manager, AppRoles.SuperUser);
        var showFz = showClearance;
        var showTrucks = InAnyRole(AppRoles.LogisticsOfficer, AppRoles.Manager, AppRoles.SuperUser);

        var recentPos = new List<HomePoRow>();
        if (showPos)
        {
            var poQuery = _db.PurchaseOrders.Where(p => p.CreatedAt >= windowStart.ToDateTime(TimeOnly.MinValue) && p.CreatedAt <= windowEnd.ToDateTime(TimeOnly.MaxValue))
                .Include(p => p.BusinessUnit).Include(p => p.Supplier).AsQueryable();
            if (!seesAllBus) poQuery = poQuery.Where(p => allowedBuIds!.Contains(p.BusinessUnitId));
            recentPos = (await poQuery
                .Select(p => new HomePoRow(p.BusinessUnit!.Name, p.PoNumber, p.Supplier!.Name, DateOnly.FromDateTime(p.CreatedAt)))
                .ToListAsync())
                .OrderByDescending(p => p.TargetDate).ToList();
        }

        var recentShipments = new List<HomeShipmentRow>();
        var arrivedArriving = new List<HomeShipmentRow>();
        var clearedAboutToClear = new List<HomeShipmentRow>();
        var recentlyDeposited = new List<HomeShipmentRow>();
        var withdrawnAboutToWithdraw = new List<HomeShipmentRow>();

        if (showShipments || showArrivals || showClearance || showFz)
        {
            var shipQuery = _db.Shipments.Where(s => s.Status != ShipmentStatus.Cancelled)
                .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
                .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
                .Include(s => s.ShippingLine)
                .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
                .AsQueryable();
            if (!seesAllBus) shipQuery = shipQuery.Where(s => allowedBuIds!.Contains(s.PurchaseOrder!.BusinessUnitId));
            var shipments = await shipQuery.ToListAsync();
            var shipmentIds = shipments.Select(s => s.Id).ToList();

            if (showShipments)
            {
                recentShipments = shipments
                    .Where(s => DateOnly.FromDateTime(s.CreatedAt) >= windowStart && DateOnly.FromDateTime(s.CreatedAt) <= windowEnd)
                    .Select(s => new HomeShipmentRow(
                        s.PurchaseOrder?.BusinessUnit?.Name ?? "", s.PurchaseOrder?.Consignee?.Name ?? "",
                        s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", s.BlAwbNo,
                        s.PurchaseOrder?.PoNumber ?? "", DateOnly.FromDateTime(s.CreatedAt), true))
                    .OrderByDescending(r => r.TargetDate).ToList();
            }

            var deliveryOrders = await _db.ClearanceDeliveryOrders.Where(d => shipmentIds.Contains(d.Clearance!.ShipmentId))
                .Include(d => d.Clearance).ToDictionaryAsync(d => d.Clearance!.ShipmentId);

            if (showArrivals)
            {
                foreach (var s in shipments)
                {
                    if (!s.Eta.HasValue) continue;
                    var actualArrival = deliveryOrders.GetValueOrDefault(s.Id)?.ActualArrivalDate;
                    var target = actualArrival ?? s.Eta.Value;
                    if (target < windowStart || target > windowEnd) continue;
                    arrivedArriving.Add(new HomeShipmentRow(
                        s.PurchaseOrder?.BusinessUnit?.Name ?? "", s.PurchaseOrder?.Consignee?.Name ?? "",
                        s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", s.BlAwbNo,
                        s.ShippingLine?.Name ?? "", target, actualArrival.HasValue));
                }
                arrivedArriving = arrivedArriving.OrderBy(r => r.TargetDate).ToList();
            }

            if (showClearance || showFz)
            {
                var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
                var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
                var scheduleService = HttpContext.RequestServices.GetRequiredService<ClearanceScheduleService>();
                var route1 = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId);
                var route2 = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).Include(r => r.Destination).ToDictionaryAsync(r => r.ClearanceId);
                var route3 = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId);

                if (showClearance)
                {
                    foreach (var s in shipments)
                    {
                        if (!clearances.TryGetValue(s.Id, out var clearance)) continue;
                        DateOnly? actualComplete = clearance.Route switch
                        {
                            Models.Clearance.ClearanceRouteType.Route1ClearAtPort => route1.GetValueOrDefault(clearance.Id)?.ClearanceActualCompletedDate,
                            Models.Clearance.ClearanceRouteType.Route2FzDeposit => route2.GetValueOrDefault(clearance.Id)?.ClearanceActualCompletedDate,
                            Models.Clearance.ClearanceRouteType.Route3ClearFromFz => route3.GetValueOrDefault(clearance.Id)?.ClearanceActualCompletedDate,
                            _ => null
                        };
                        DateOnly? target = actualComplete;
                        if (!target.HasValue && clearance.Route != Models.Clearance.ClearanceRouteType.NotSelected)
                        {
                            var schedule = await scheduleService.GetScheduleAsync(s.Id);
                            target = schedule.EstimatedCompletionDate;
                        }
                        if (!target.HasValue || target.Value < windowStart || target.Value > windowEnd) continue;

                        clearedAboutToClear.Add(new HomeShipmentRow(
                            s.PurchaseOrder?.BusinessUnit?.Name ?? "", s.PurchaseOrder?.Consignee?.Name ?? "",
                            s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", s.BlAwbNo,
                            clearance.Route.ToString(), target.Value, actualComplete.HasValue));
                    }
                    clearedAboutToClear = clearedAboutToClear.OrderBy(r => r.TargetDate).ToList();
                }

                if (showFz)
                {
                    foreach (var s in shipments)
                    {
                        if (!clearances.TryGetValue(s.Id, out var clearance)) continue;
                        if (clearance.Route != Models.Clearance.ClearanceRouteType.Route2FzDeposit) continue;
                        var r2 = route2.GetValueOrDefault(clearance.Id);
                        if (r2?.ContainersReceivedAtFzDate is not { } depositDate) continue;
                        if (depositDate < windowStart || depositDate > windowEnd) continue;

                        recentlyDeposited.Add(new HomeShipmentRow(
                            s.PurchaseOrder?.BusinessUnit?.Name ?? "", s.PurchaseOrder?.Consignee?.Name ?? "",
                            s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", s.BlAwbNo,
                            r2?.Destination?.Name ?? "FZ", depositDate, true));
                    }
                    recentlyDeposited = recentlyDeposited.OrderByDescending(r => r.TargetDate).ToList();

                    var withdrawals = await _db.Withdrawals
                        .Where(w => shipmentIds.Contains(w.DepositShipmentId))
                        .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
                        .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
                        .ToListAsync();

                    foreach (var w in withdrawals)
                    {
                        if (!w.WithdrawalRequestDate.HasValue) continue;
                        if (w.WithdrawalRequestDate.Value < windowStart || w.WithdrawalRequestDate.Value > windowEnd) continue;
                        var depositShipment = w.DepositShipment;
                        if (depositShipment is null) continue;
                        clearances.TryGetValue(depositShipment.Id, out var depositClearance);
                        var fzName = depositClearance is not null ? route2.GetValueOrDefault(depositClearance.Id)?.Destination?.Name : null;

                        withdrawnAboutToWithdraw.Add(new HomeShipmentRow(
                            depositShipment.PurchaseOrder?.BusinessUnit?.Name ?? "", depositShipment.PurchaseOrder?.Consignee?.Name ?? "",
                            "", depositShipment.BlAwbNo, fzName ?? "FZ", w.WithdrawalRequestDate.Value, false));
                    }
                    withdrawnAboutToWithdraw = withdrawnAboutToWithdraw.OrderBy(r => r.TargetDate).ToList();
                }
            }
        }

        var allocatedTrucks = new List<HomeTruckRow>();
        var arrivedArrivingTrucks = new List<HomeTruckRow>();

        if (showTrucks)
        {
            var loads = await _db.TruckLoads
                .Include(tl => tl.Truck).Include(tl => tl.Driver)
                .Where(tl => tl.LoadDate >= windowStart && tl.LoadDate <= windowEnd)
                .ToListAsync();
            allocatedTrucks = loads.Select(tl => new HomeTruckRow(
                "", "", "", tl.Truck?.PlateNo ?? "", tl.Driver?.Name ?? "", tl.LoadDate, true))
                .OrderByDescending(r => r.TargetDate).ToList();

            var drops = await _db.TruckLoadDrops
                .Include(d => d.TruckLoad).ThenInclude(tl => tl!.Truck)
                .Include(d => d.Warehouse).ThenInclude(w => w!.City)
                .ToListAsync();

            foreach (var d in drops)
            {
                var target = d.ActualDropOffDate ?? d.ExpectedDeliveryDate;
                if (!target.HasValue || target.Value < windowStart || target.Value > windowEnd) continue;
                arrivedArrivingTrucks.Add(new HomeTruckRow(
                    "", "", "", d.TruckLoad?.Truck?.PlateNo ?? "",
                    $"{d.Warehouse?.Name} ({d.Warehouse?.City?.Name})", target.Value, d.ActualDropOffDate.HasValue));
            }
            arrivedArrivingTrucks = arrivedArrivingTrucks.OrderBy(r => r.TargetDate).ToList();
        }

        var redAlertCount = 0;
        if (showArrivals || showClearance)
        {
            var readinessService = HttpContext.RequestServices.GetRequiredService<PreClearanceReadinessService>();
            var allShipmentIds = await _db.Shipments.Where(s => s.Status == ShipmentStatus.Confirmed).Select(s => s.Id).ToListAsync();
            var readiness = await readinessService.CalculateAsync(allShipmentIds);
            redAlertCount = readiness.Count(r => r.Classification == "Red");
        }

        return Ok(new HomePageResponse(
            showPos, recentPos,
            showShipments, recentShipments,
            showArrivals, arrivedArriving,
            showClearance, clearedAboutToClear,
            showFz, recentlyDeposited, withdrawnAboutToWithdraw,
            showTrucks, allocatedTrucks, arrivedArrivingTrucks,
            redAlertCount));
    }
}
