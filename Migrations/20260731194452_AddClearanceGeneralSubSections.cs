using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddClearanceGeneralSubSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClearanceCertificateEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    CertificateEntryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScudaDeclarationNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceCertificateEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceCertificateEntries_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceCostEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    EstimateDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EstimateValueSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    NotifyBuDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AmountSettledDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceCostEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceCostEstimates_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceDeliveryOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    CopyOfDoCollectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceiveDoDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualArrivalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DoFeesSdg = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    DoFeesSettledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DoReceivedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceDeliveryOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceDeliveryOrders_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceCertificateEntries_ClearanceId",
                table: "ClearanceCertificateEntries",
                column: "ClearanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceCostEstimates_ClearanceId",
                table: "ClearanceCostEstimates",
                column: "ClearanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceDeliveryOrders_ClearanceId",
                table: "ClearanceDeliveryOrders",
                column: "ClearanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceCertificateEntries");

            migrationBuilder.DropTable(
                name: "ClearanceCostEstimates");

            migrationBuilder.DropTable(
                name: "ClearanceDeliveryOrders");
        }
    }
}
