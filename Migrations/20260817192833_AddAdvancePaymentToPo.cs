using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancePaymentToPo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFromPoAdvance",
                table: "ShipmentPaymentDues",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AdvancePaymentExecutedDate",
                table: "PurchaseOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvancePaymentPercent",
                table: "PurchaseOrders",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AdvancePaymentPlannedDate",
                table: "PurchaseOrders",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFromPoAdvance",
                table: "ShipmentPaymentDues");

            migrationBuilder.DropColumn(
                name: "AdvancePaymentExecutedDate",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "AdvancePaymentPercent",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "AdvancePaymentPlannedDate",
                table: "PurchaseOrders");
        }
    }
}
