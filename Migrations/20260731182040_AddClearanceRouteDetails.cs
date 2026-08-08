using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddClearanceRouteDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearanceRoute1Details",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    MoveRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BillAmountSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    BillSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
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
                    SpcBillRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SpcBillValueSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SpcBillSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TruckPortEntryPermitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContainersReturnedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClearanceActualCompletedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceRoute1Details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceRoute1Details_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceRoute2Details",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    DepositRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestApprovalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InspectionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SpcBillRequestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SpcBillValueSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SpcBillSettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PoliceSecurityAppointedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TruckPortEntryPermitDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContainersReceivedAtFzDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContainersReturnedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClearanceActualCompletedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceRoute2Details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceRoute2Details_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceRoute3Details",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
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
                    ClearanceActualCompletedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceRoute3Details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceRoute3Details_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute1Details_ClearanceId",
                table: "ClearanceRoute1Details",
                column: "ClearanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute2Details_ClearanceId",
                table: "ClearanceRoute2Details",
                column: "ClearanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute3Details_ClearanceId",
                table: "ClearanceRoute3Details",
                column: "ClearanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceRoute1Details");

            migrationBuilder.DropTable(
                name: "ClearanceRoute2Details");

            migrationBuilder.DropTable(
                name: "ClearanceRoute3Details");
        }
    }
}
