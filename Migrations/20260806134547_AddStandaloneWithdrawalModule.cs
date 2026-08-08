using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddStandaloneWithdrawalModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DepositShipmentId = table.Column<int>(type: "int", nullable: false),
                    WithdrawalRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WithdrawalRequestRefNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CertificateEntryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScudaDeclarationNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SsmoFileRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SsmoInspectionAmountSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SsmoFeesSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustExamStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustExamCompletedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomsLabRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CustomsLabFeesSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    LabFeesPaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LabResultIssuanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SsmoExamStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SsmoCertIssuanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustEvaluationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomsDutySdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CustomsSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReleaseExitPassDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TruckPortEntryPermitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClearanceActualCompletedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Withdrawals_Shipments_DepositShipmentId",
                        column: x => x.DepositShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithdrawalCostEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WithdrawalId = table.Column<int>(type: "int", nullable: false),
                    EstimateDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NotifyBuDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AmountSettledDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalCostEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalCostEstimates_Withdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "Withdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithdrawalEstimateLineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WithdrawalId = table.Column<int>(type: "int", nullable: false),
                    ChargeTypeId = table.Column<int>(type: "int", nullable: false),
                    ValueSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalEstimateLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalEstimateLineItems_ClearanceChargeTypes_ChargeTypeId",
                        column: x => x.ChargeTypeId,
                        principalTable: "ClearanceChargeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WithdrawalEstimateLineItems_Withdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "Withdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithdrawalLineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WithdrawalId = table.Column<int>(type: "int", nullable: false),
                    DepositShipmentLineItemId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalLineItems_ShipmentLineItems_DepositShipmentLineIte~",
                        column: x => x.DepositShipmentLineItemId,
                        principalTable: "ShipmentLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WithdrawalLineItems_Withdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "Withdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalCostEstimates_WithdrawalId",
                table: "WithdrawalCostEstimates",
                column: "WithdrawalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalEstimateLineItems_ChargeTypeId",
                table: "WithdrawalEstimateLineItems",
                column: "ChargeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalEstimateLineItems_WithdrawalId",
                table: "WithdrawalEstimateLineItems",
                column: "WithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalLineItems_DepositShipmentLineItemId",
                table: "WithdrawalLineItems",
                column: "DepositShipmentLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalLineItems_WithdrawalId",
                table: "WithdrawalLineItems",
                column: "WithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_DepositShipmentId",
                table: "Withdrawals",
                column: "DepositShipmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WithdrawalCostEstimates");

            migrationBuilder.DropTable(
                name: "WithdrawalEstimateLineItems");

            migrationBuilder.DropTable(
                name: "WithdrawalLineItems");

            migrationBuilder.DropTable(
                name: "Withdrawals");
        }
    }
}
