using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiPoAndDirectSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConsigneeName",
                table: "Shipments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsDirectSales",
                table: "Shipments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ShipmentCustomerDues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCustomerDues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentCustomerDues_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentCustomerDues_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentPurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPurchaseOrders_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPurchaseOrders_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCustomerDues_CurrencyId",
                table: "ShipmentCustomerDues",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCustomerDues_ShipmentId",
                table: "ShipmentCustomerDues",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPurchaseOrders_PurchaseOrderId",
                table: "ShipmentPurchaseOrders",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPurchaseOrders_ShipmentId_PurchaseOrderId",
                table: "ShipmentPurchaseOrders",
                columns: new[] { "ShipmentId", "PurchaseOrderId" },
                unique: true);

            // Backfill: shipments created before this migration only have
            // Shipment.PurchaseOrderId set — without this, they'd be invisible
            // to the PO Dashboard / Department Performance queries, which now
            // read from this join table only.
            migrationBuilder.Sql(@"
                INSERT INTO ShipmentPurchaseOrders (ShipmentId, PurchaseOrderId)
                SELECT Id, PurchaseOrderId FROM Shipments WHERE PurchaseOrderId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentCustomerDues");

            migrationBuilder.DropTable(
                name: "ShipmentPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ConsigneeName",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "IsDirectSales",
                table: "Shipments");
        }
    }
}