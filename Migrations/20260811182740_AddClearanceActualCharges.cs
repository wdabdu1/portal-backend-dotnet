using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddClearanceActualCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearanceActualCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    ForecastDemurrageSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ForecastStorageSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ForecastCapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActualDemurragePaidSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ActualStoragePaidSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ShippingLineDepositReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AmountReturnedFromDeposit = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceActualCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceActualCharges_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceActualCharges_ClearanceId",
                table: "ClearanceActualCharges",
                column: "ClearanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceActualCharges");
        }
    }
}
