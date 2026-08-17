using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

// Wraps mysqldump/mysql directly rather than a hand-rolled export —
// the industry-standard tool for exactly this job, and it correctly
// handles schema, data, and foreign-key ordering without us having to
// reimplement any of that. SuperUser-only, given both directions of
// this feature are powerful: backup can expose the entire database's
// contents, and restore can silently overwrite it.
[ApiController]
[Route("api/admin/database-backup")]
[Authorize(Roles = AppRoles.SuperUser)]
public class DatabaseBackupController : ControllerBase
{
    private readonly IConfiguration _config;
    public DatabaseBackupController(IConfiguration config) => _config = config;

    private (string Host, string Port, string Database, string User, string Password) ParseConnectionString()
    {
        var raw = _config["CONNECTION_STRING"] ?? throw new InvalidOperationException("CONNECTION_STRING not configured.");
        var builder = new MySqlConnectionStringBuilder(raw);
        return (builder.Server, builder.Port.ToString(), builder.Database, builder.UserID, builder.Password);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var (host, port, database, user, password) = ParseConnectionString();

        var psi = new ProcessStartInfo
        {
            FileName = "mysqldump",
            ArgumentList =
            {
                $"--host={host}", $"--port={port}", $"--user={user}",
                "--single-transaction", "--routines", "--triggers", "--column-statistics=0",
                database
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            EnvironmentVariables = { ["MYSQL_PWD"] = password } // avoids exposing the password in process args/listings
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start mysqldump.");
        var output = new MemoryStream();
        var outputCopyTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errorTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(outputCopyTask, process.WaitForExitAsync());
        var stderr = await errorTask;

        if (process.ExitCode != 0)
            return StatusCode(500, $"mysqldump failed (exit {process.ExitCode}): {stderr}");

        output.Position = 0;
        var fileName = $"backup-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.sql";
        return File(output, "application/sql", fileName);
    }

    public record RestoreRequest(string ConfirmationPhrase);

    // Deliberately requires typing an exact phrase, not just a role
    // check — restore can silently overwrite the entire live database,
    // and that shouldn't be one click away for anyone, SuperUser or not.
    [HttpPost("restore")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Restore(IFormFile file, [FromForm] string confirmationPhrase)
    {
        if (confirmationPhrase != "RESTORE DATABASE")
            return BadRequest("Confirmation phrase did not match. Type exactly: RESTORE DATABASE");

        if (file is null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var (host, port, database, user, password) = ParseConnectionString();

        var psi = new ProcessStartInfo
        {
            FileName = "mysql",
            ArgumentList = { $"--host={host}", $"--port={port}", $"--user={user}", database },
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            EnvironmentVariables = { ["MYSQL_PWD"] = password }
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start mysql client.");
        using (var uploadStream = file.OpenReadStream())
        {
            await uploadStream.CopyToAsync(process.StandardInput.BaseStream);
        }
        process.StandardInput.Close();

        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stderr = await errorTask;

        if (process.ExitCode != 0)
            return StatusCode(500, $"Restore failed (exit {process.ExitCode}): {stderr}");

        return Ok(new { message = "Restore completed successfully." });
    }
}
