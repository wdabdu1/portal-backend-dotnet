using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class TariffGroupsAndStorageTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuGroup",
                table: "ShippingLineDemurrageTariffs");

            migrationBuilder.RenameColumn(
                name: "FirstPeriodRate",
                table: "ShippingLineDemurrageTariffs",
                newName: "FirstPeriodRateSdg");

            migrationBuilder.RenameColumn(
                name: "AfterwardRate",
                table: "ShippingLineDemurrageTariffs",
                newName: "AfterwardRateSdg");

            migrationBuilder.AddColumn<int>(
                name: "TariffGroupId",
                table: "ShippingLineDemurrageTariffs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TariffGroupId",
                table: "ProductCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpcStorageTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TierOrder = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DurationDays = table.Column<int>(type: "int", nullable: true),
                    Rate20 = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Rate40 = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpcStorageTiers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TariffGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffGroups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingLineDemurrageTariffs_TariffGroupId",
                table: "ShippingLineDemurrageTariffs",
                column: "TariffGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_TariffGroupId",
                table: "ProductCategories",
                column: "TariffGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductCategories_TariffGroups_TariffGroupId",
                table: "ProductCategories",
                column: "TariffGroupId",
                principalTable: "TariffGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ShippingLineDemurrageTariffs_TariffGroups_TariffGroupId",
                table: "ShippingLineDemurrageTariffs",
                column: "TariffGroupId",
                principalTable: "TariffGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductCategories_TariffGroups_TariffGroupId",
                table: "ProductCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ShippingLineDemurrageTariffs_TariffGroups_TariffGroupId",
                table: "ShippingLineDemurrageTariffs");

            migrationBuilder.DropTable(
                name: "SpcStorageTiers");

            migrationBuilder.DropTable(
                name: "TariffGroups");

            migrationBuilder.DropIndex(
                name: "IX_ShippingLineDemurrageTariffs_TariffGroupId",
                table: "ShippingLineDemurrageTariffs");

            migrationBuilder.DropIndex(
                name: "IX_ProductCategories_TariffGroupId",
                table: "ProductCategories");

            migrationBuilder.DropColumn(
                name: "TariffGroupId",
                table: "ShippingLineDemurrageTariffs");

            migrationBuilder.DropColumn(
                name: "TariffGroupId",
                table: "ProductCategories");

            migrationBuilder.RenameColumn(
                name: "FirstPeriodRateSdg",
                table: "ShippingLineDemurrageTariffs",
                newName: "FirstPeriodRate");

            migrationBuilder.RenameColumn(
                name: "AfterwardRateSdg",
                table: "ShippingLineDemurrageTariffs",
                newName: "AfterwardRate");

            migrationBuilder.AddColumn<string>(
                name: "BuGroup",
                table: "ShippingLineDemurrageTariffs",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
