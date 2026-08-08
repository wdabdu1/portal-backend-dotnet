using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class ClearanceGeneralInfoAndCostEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImFormDate",
                table: "ShipmentBankings");

            migrationBuilder.DropColumn(
                name: "ImFormNo",
                table: "ShipmentBankings");

            migrationBuilder.DropColumn(
                name: "EstimateValueSdg",
                table: "ClearanceCostEstimates");

            migrationBuilder.RenameColumn(
                name: "DoFeesSdg",
                table: "ClearanceDeliveryOrders",
                newName: "DoActualFeesSdg");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ImFormDate",
                table: "Clearances",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImFormNo",
                table: "Clearances",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "DepositValue",
                table: "ClearanceRoute2Details",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShippingLineDepositReturnDate",
                table: "ClearanceRoute2Details",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositValue",
                table: "ClearanceRoute1Details",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShippingLineDepositReturnDate",
                table: "ClearanceRoute1Details",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DepositRequired",
                table: "ClearanceDeliveryOrders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClearanceChargeTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceChargeTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceEstimateLineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    ChargeTypeId = table.Column<int>(type: "int", nullable: false),
                    ValueSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceEstimateLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceEstimateLineItems_ClearanceChargeTypes_ChargeTypeId",
                        column: x => x.ChargeTypeId,
                        principalTable: "ClearanceChargeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClearanceEstimateLineItems_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceEstimateLineItems_ChargeTypeId",
                table: "ClearanceEstimateLineItems",
                column: "ChargeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceEstimateLineItems_ClearanceId",
                table: "ClearanceEstimateLineItems",
                column: "ClearanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceEstimateLineItems");

            migrationBuilder.DropTable(
                name: "ClearanceChargeTypes");

            migrationBuilder.DropColumn(
                name: "ImFormDate",
                table: "Clearances");

            migrationBuilder.DropColumn(
                name: "ImFormNo",
                table: "Clearances");

            migrationBuilder.DropColumn(
                name: "DepositValue",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropColumn(
                name: "ShippingLineDepositReturnDate",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropColumn(
                name: "DepositValue",
                table: "ClearanceRoute1Details");

            migrationBuilder.DropColumn(
                name: "ShippingLineDepositReturnDate",
                table: "ClearanceRoute1Details");

            migrationBuilder.DropColumn(
                name: "DepositRequired",
                table: "ClearanceDeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "DoActualFeesSdg",
                table: "ClearanceDeliveryOrders",
                newName: "DoFeesSdg");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ImFormDate",
                table: "ShipmentBankings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImFormNo",
                table: "ShipmentBankings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimateValueSdg",
                table: "ClearanceCostEstimates",
                type: "decimal(65,30)",
                nullable: true);
        }
    }
}
