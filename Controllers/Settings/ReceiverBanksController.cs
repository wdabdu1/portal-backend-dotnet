using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

[ApiController]
[Authorize]
[Route("api/settings/receiver-banks")]
public class ReceiverBanksControllerCustom : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ReceiverBanksControllerCustom(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiverBank>>> GetAll() => await _db.ReceiverBanks.ToListAsync();

    [HttpPost]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ReceiverBank>> Create(ReceiverBank bank)
    {
        bank.TotalChargeRate = bank.BankChargeRate + bank.ImChargeRate;
        _db.ReceiverBanks.Add(bank);
        await _db.SaveChangesAsync();
        return Ok(bank);
    }
}
