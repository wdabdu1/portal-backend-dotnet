using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;

namespace ShippingPortal.Api.Services;

// Wipes every real data/Settings table, discovered directly from the
// database itself rather than a hand-maintained list — so it can never
// silently miss a table after a future migration adds one. User
// accounts, roles, and login data (the AspNet* Identity tables) and
// the EF migration history table are explicitly protected and never
// touched, regardless of what else exists in the schema.
public class CompleteDeleteService
{
    private readonly ShippingPortalDbContext _db;
    public CompleteDeleteService(ShippingPortalDbContext db) => _db = db;

    private static readonly string[] ProtectedTables =
    {
        "__EFMigrationsHistory",
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
        "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims"
    };

    public async Task<List<string>> DeleteAllAsync()
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        var dbName = conn.Database;
        var allTables = new List<string>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_TYPE = 'BASE TABLE'";
            var param = cmd.CreateParameter();
            param.ParameterName = "@db";
            param.Value = dbName;
            cmd.Parameters.Add(param);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                allTables.Add(reader.GetString(0));
        }

        var tablesToWipe = allTables.Where(t => !ProtectedTables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();

        using (var offCmd = conn.CreateCommand())
        {
            offCmd.CommandText = "SET FOREIGN_KEY_CHECKS = 0;";
            await offCmd.ExecuteNonQueryAsync();
        }

        foreach (var table in tablesToWipe)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"TRUNCATE TABLE `{table}`;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var onCmd = conn.CreateCommand())
        {
            onCmd.CommandText = "SET FOREIGN_KEY_CHECKS = 1;";
            await onCmd.ExecuteNonQueryAsync();
        }

        return tablesToWipe;
    }
}
