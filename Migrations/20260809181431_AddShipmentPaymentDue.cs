using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentPaymentDue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentDueId",
                table: "ShipmentSupplierPaymentRecords",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShipmentPaymentDues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentPaymentDues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentPaymentDues_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentPaymentDues_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPaymentRecords_PaymentDueId",
                table: "ShipmentSupplierPaymentRecords",
                column: "PaymentDueId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPaymentDues_CurrencyId",
                table: "ShipmentPaymentDues",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentPaymentDues_ShipmentId",
                table: "ShipmentPaymentDues",
                column: "ShipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_ShipmentPaymentDues_PaymentDu~",
                table: "ShipmentSupplierPaymentRecords",
                column: "PaymentDueId",
                principalTable: "ShipmentPaymentDues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_ShipmentPaymentDues_PaymentDu~",
                table: "ShipmentSupplierPaymentRecords");

            migrationBuilder.DropTable(
                name: "ShipmentPaymentDues");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentSupplierPaymentRecords_PaymentDueId",
                table: "ShipmentSupplierPaymentRecords");

            migrationBuilder.DropColumn(
                name: "PaymentDueId",
                table: "ShipmentSupplierPaymentRecords");
        }
    }
}
