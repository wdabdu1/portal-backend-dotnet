using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

// A Receiver Bank can hold several of the company's own accounts (e.g.
// one per currency or purpose) — this is what lets the Pay Bank Dues
// letter show the exact right Account No. + Account Name together,
// rather than risking the wrong one being typed in freehand.
[ApiController]
[Authorize]
[Route("api/settings/receiver-banks/{bankId:int}/accounts")]
public class ReceiverBankAccountsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ReceiverBankAccountsController(ShippingPortalDbContext db) => _db = db;

    public record AccountRequest(string AccountNo, string AccountName, bool IsActive);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiverBankAccount>>> GetAll(int bankId) =>
        await _db.ReceiverBankAccounts.Where(a => a.ReceiverBankId == bankId).ToListAsync();

    [HttpPost]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ReceiverBankAccount>> Create(int bankId, AccountRequest req)
    {
        var bankExists = await _db.ReceiverBanks.AnyAsync(b => b.Id == bankId);
        if (!bankExists) return NotFound(new { message = "Receiver Bank not found." });

        var account = new ReceiverBankAccount
        {
            ReceiverBankId = bankId,
            AccountNo = req.AccountNo,
            AccountName = req.AccountName,
            IsActive = req.IsActive
        };
        _db.ReceiverBankAccounts.Add(account);
        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ReceiverBankAccount>> Update(int bankId, int id, AccountRequest req)
    {
        var account = await _db.ReceiverBankAccounts.FirstOrDefaultAsync(a => a.Id == id && a.ReceiverBankId == bankId);
        if (account is null) return NotFound();

        account.AccountNo = req.AccountNo;
        account.AccountName = req.AccountName;
        account.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Delete(int bankId, int id)
    {
        var account = await _db.ReceiverBankAccounts.FirstOrDefaultAsync(a => a.Id == id && a.ReceiverBankId == bankId);
        if (account is null) return NotFound();

        _db.ReceiverBankAccounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
