using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPaymentLedgerAndSob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_CurrencyId",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "AdvanceActualPaymentDate",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "AdvanceDueDate",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "AdvanceValue",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "AdvanceValueUsd",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "RemainingActualPaymentDate",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "RemainingDueDate",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "RemainingValue",
                table: "ShipmentSupplierPayments");

            migrationBuilder.RenameColumn(
                name: "TotalValueUsd",
                table: "ShipmentSupplierPayments",
                newName: "InvoiceValueUsd");

            migrationBuilder.RenameColumn(
                name: "RemainingValueUsd",
                table: "ShipmentSupplierPayments",
                newName: "InvoiceValue");

            migrationBuilder.RenameColumn(
                name: "CurrencyId",
                table: "ShipmentSupplierPayments",
                newName: "InvoiceCurrencyId");

            migrationBuilder.RenameIndex(
                name: "IX_ShipmentSupplierPayments_CurrencyId",
                table: "ShipmentSupplierPayments",
                newName: "IX_ShipmentSupplierPayments_InvoiceCurrencyId");

            migrationBuilder.AddColumn<string>(
                name: "SupplierInvoiceNo",
                table: "ShipmentSupplierPayments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "SobActualDate",
                table: "Shipments",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShipmentSupplierPaymentRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentSupplierPaymentId = table.Column<int>(type: "int", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ValueUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSupplierPaymentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPaymentRecords_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPaymentRecords_ShipmentSupplierPayments_Ship~",
                        column: x => x.ShipmentSupplierPaymentId,
                        principalTable: "ShipmentSupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPaymentRecords_CurrencyId",
                table: "ShipmentSupplierPaymentRecords",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPaymentRecords_ShipmentSupplierPaymentId",
                table: "ShipmentSupplierPaymentRecords",
                column: "ShipmentSupplierPaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_InvoiceCurrencyId",
                table: "ShipmentSupplierPayments",
                column: "InvoiceCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_InvoiceCurrencyId",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropTable(
                name: "ShipmentSupplierPaymentRecords");

            migrationBuilder.DropColumn(
                name: "SupplierInvoiceNo",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "SobActualDate",
                table: "Shipments");

            migrationBuilder.RenameColumn(
                name: "InvoiceValueUsd",
                table: "ShipmentSupplierPayments",
                newName: "TotalValueUsd");

            migrationBuilder.RenameColumn(
                name: "InvoiceValue",
                table: "ShipmentSupplierPayments",
                newName: "RemainingValueUsd");

            migrationBuilder.RenameColumn(
                name: "InvoiceCurrencyId",
                table: "ShipmentSupplierPayments",
                newName: "CurrencyId");

            migrationBuilder.RenameIndex(
                name: "IX_ShipmentSupplierPayments_InvoiceCurrencyId",
                table: "ShipmentSupplierPayments",
                newName: "IX_ShipmentSupplierPayments_CurrencyId");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AdvanceActualPaymentDate",
                table: "ShipmentSupplierPayments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AdvanceDueDate",
                table: "ShipmentSupplierPayments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceValue",
                table: "ShipmentSupplierPayments",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceValueUsd",
                table: "ShipmentSupplierPayments",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RemainingActualPaymentDate",
                table: "ShipmentSupplierPayments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RemainingDueDate",
                table: "ShipmentSupplierPayments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingValue",
                table: "ShipmentSupplierPayments",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_CurrencyId",
                table: "ShipmentSupplierPayments",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id");
        }
    }
}
