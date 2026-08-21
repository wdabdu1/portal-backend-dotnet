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

    // Previously missing entirely — there was no way to edit an existing
    // Receiver Bank at all, including adding its Address after the fact.
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ReceiverBank>> Update(int id, ReceiverBank req)
    {
        var bank = await _db.ReceiverBanks.FirstOrDefaultAsync(b => b.Id == id);
        if (bank is null) return NotFound();

        bank.Name = req.Name;
        bank.BankChargeRate = req.BankChargeRate;
        bank.ImChargeRate = req.ImChargeRate;
        bank.TotalChargeRate = req.BankChargeRate + req.ImChargeRate;
        bank.IsActive = req.IsActive;
        bank.Address = req.Address;
        await _db.SaveChangesAsync();
        return Ok(bank);
    }
}
