using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShippingPortal.Api.Data;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    // Makes (PurchaseOrderId, SequenceOrder) unique on PurchaseOrderOffshorePartners.
    //
    // Guarded on purpose (information_schema + dynamic SQL), so deploying can
    // never take the app down:
    //  - the unique index is only created if it doesn't exist yet AND no
    //    duplicate (PO, Sequence) rows remain - if any do, it is skipped
    //    (clean them up with cleanup_duplicate_offshore_chain.sql and the
    //    index can be added by re-running the CREATE UNIQUE INDEX below);
    //  - the old single-column PurchaseOrderId index is dropped only after the
    //    new index is in place (MySQL needs an index on the FK column), because
    //    the composite index now serves as the FK index.
    [DbContext(typeof(ShippingPortalDbContext))]
    [Migration("20261019090000_AddUniqueOffshoreChainSequence")]
    public partial class AddUniqueOffshoreChainSequence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @has_dups = (SELECT COUNT(*) FROM (
    SELECT 1 FROM PurchaseOrderOffshorePartners
    GROUP BY PurchaseOrderId, SequenceOrder HAVING COUNT(*) > 1) d);
SET @new_exists = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'PurchaseOrderOffshorePartners'
      AND index_name = 'IX_PurchaseOrderOffshorePartners_PurchaseOrderId_SequenceOrder');
SET @stmt = IF(@has_dups = 0 AND @new_exists = 0,
    'CREATE UNIQUE INDEX IX_PurchaseOrderOffshorePartners_PurchaseOrderId_SequenceOrder ON PurchaseOrderOffshorePartners (PurchaseOrderId, SequenceOrder)',
    'SELECT 1');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;
");

            migrationBuilder.Sql(@"
SET @new_ok = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'PurchaseOrderOffshorePartners'
      AND index_name = 'IX_PurchaseOrderOffshorePartners_PurchaseOrderId_SequenceOrder');
SET @old_exists = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'PurchaseOrderOffshorePartners'
      AND index_name = 'IX_PurchaseOrderOffshorePartners_PurchaseOrderId');
SET @stmt = IF(@new_ok > 0 AND @old_exists > 0,
    'DROP INDEX IX_PurchaseOrderOffshorePartners_PurchaseOrderId ON PurchaseOrderOffshorePartners',
    'SELECT 1');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @old_exists = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'PurchaseOrderOffshorePartners'
      AND index_name = 'IX_PurchaseOrderOffshorePartners_PurchaseOrderId');
SET @stmt = IF(@old_exists = 0,
    'CREATE INDEX IX_PurchaseOrderOffshorePartners_PurchaseOrderId ON PurchaseOrderOffshorePartners (PurchaseOrderId)',
    'SELECT 1');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;
");

            migrationBuilder.Sql(@"
SET @new_exists = (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'PurchaseOrderOffshorePartners'
      AND index_name = 'IX_PurchaseOrderOffshorePartners_PurchaseOrderId_SequenceOrder');
SET @stmt = IF(@new_exists > 0,
    'DROP INDEX IX_PurchaseOrderOffshorePartners_PurchaseOrderId_SequenceOrder ON PurchaseOrderOffshorePartners',
    'SELECT 1');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;
");
        }
    }
}
