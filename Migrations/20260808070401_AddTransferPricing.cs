using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferPricingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentLineItemId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderOffshorePartnerId = table.Column<int>(type: "int", nullable: false),
                    MarkupPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferPricingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferPricingEntries_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferPricingEntries_PurchaseOrderOffshorePartners_Purchas~",
                        column: x => x.PurchaseOrderOffshorePartnerId,
                        principalTable: "PurchaseOrderOffshorePartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferPricingEntries_ShipmentLineItems_ShipmentLineItemId",
                        column: x => x.ShipmentLineItemId,
                        principalTable: "ShipmentLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TransferPricingEntries_CurrencyId",
                table: "TransferPricingEntries",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferPricingEntries_PurchaseOrderOffshorePartnerId",
                table: "TransferPricingEntries",
                column: "PurchaseOrderOffshorePartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferPricingEntries_ShipmentLineItemId_PurchaseOrderOffsh~",
                table: "TransferPricingEntries",
                columns: new[] { "ShipmentLineItemId", "PurchaseOrderOffshorePartnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferPricingEntries");
        }
    }
}
