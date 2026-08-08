using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSupplierPaymentStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentSupplierPayments_PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments");

            migrationBuilder.RenameColumn(
                name: "PaymentExecutedValue",
                table: "ShipmentSupplierPayments",
                newName: "TotalValueUsd");

            migrationBuilder.RenameColumn(
                name: "PaymentExecutedUsd",
                table: "ShipmentSupplierPayments",
                newName: "TotalPaidUsd");

            migrationBuilder.RenameColumn(
                name: "PaymentExecutedDate",
                table: "ShipmentSupplierPayments",
                newName: "RemainingDueDate");

            migrationBuilder.RenameColumn(
                name: "DueDate",
                table: "ShipmentSupplierPayments",
                newName: "RemainingActualPaymentDate");

            migrationBuilder.RenameColumn(
                name: "DueBalanceUsd",
                table: "ShipmentSupplierPayments",
                newName: "RemainingValueUsd");

            migrationBuilder.RenameColumn(
                name: "DueAmountUsd",
                table: "ShipmentSupplierPayments",
                newName: "RemainingValue");

            migrationBuilder.RenameColumn(
                name: "DueAmount",
                table: "ShipmentSupplierPayments",
                newName: "BalanceUsd");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.RenameColumn(
                name: "TotalValueUsd",
                table: "ShipmentSupplierPayments",
                newName: "PaymentExecutedValue");

            migrationBuilder.RenameColumn(
                name: "TotalPaidUsd",
                table: "ShipmentSupplierPayments",
                newName: "PaymentExecutedUsd");

            migrationBuilder.RenameColumn(
                name: "RemainingValueUsd",
                table: "ShipmentSupplierPayments",
                newName: "DueBalanceUsd");

            migrationBuilder.RenameColumn(
                name: "RemainingValue",
                table: "ShipmentSupplierPayments",
                newName: "DueAmountUsd");

            migrationBuilder.RenameColumn(
                name: "RemainingDueDate",
                table: "ShipmentSupplierPayments",
                newName: "PaymentExecutedDate");

            migrationBuilder.RenameColumn(
                name: "RemainingActualPaymentDate",
                table: "ShipmentSupplierPayments",
                newName: "DueDate");

            migrationBuilder.RenameColumn(
                name: "BalanceUsd",
                table: "ShipmentSupplierPayments",
                newName: "DueAmount");

            migrationBuilder.AddColumn<int>(
                name: "PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPayments_PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments",
                column: "PaymentExecutedCurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPayments_Currencies_PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments",
                column: "PaymentExecutedCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id");
        }
    }
}
