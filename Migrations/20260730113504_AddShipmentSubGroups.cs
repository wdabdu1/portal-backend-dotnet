using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentSubGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentAcds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ProcessDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CostUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CostSettledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RefNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentAcds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentAcds_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentBankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    SenderBankId = table.Column<int>(type: "int", nullable: true),
                    OsDocDispatchDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OsDocDispatchedViaId = table.Column<int>(type: "int", nullable: true),
                    OsDocTrackingNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderBankCharges = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ReceivingBankId = table.Column<int>(type: "int", nullable: true),
                    NecessaryGoodType = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CollectionRefNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CollectionValue = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CollectionCurrencyId = table.Column<int>(type: "int", nullable: true),
                    TenorId = table.Column<int>(type: "int", nullable: true),
                    CollectionDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CollectionAmountSettled = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    RemainingDues = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ImFormNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImFormDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceiverBankCharges = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentBankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_Couriers_OsDocDispatchedViaId",
                        column: x => x.OsDocDispatchedViaId,
                        principalTable: "Couriers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_Currencies_CollectionCurrencyId",
                        column: x => x.CollectionCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_ReceiverBanks_ReceivingBankId",
                        column: x => x.ReceivingBankId,
                        principalTable: "ReceiverBanks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_SenderBanks_SenderBankId",
                        column: x => x.SenderBankId,
                        principalTable: "SenderBanks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShipmentBankings_Tenors_TenorId",
                        column: x => x.TenorId,
                        principalTable: "Tenors",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentDraftDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    InitialDraftReceivedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FinalDraftReceivedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FinalDraftConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentDraftDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentDraftDocuments_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentForwarders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ForwarderId = table.Column<int>(type: "int", nullable: true),
                    ActualShippingCost = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    ActualShippingCostUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    AmountSaved = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    MarineInsurance = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentForwarders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentForwarders_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentForwarders_Forwarders_ForwarderId",
                        column: x => x.ForwarderId,
                        principalTable: "Forwarders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentForwarders_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentMots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ProcessDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CostSettledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RefNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OffshoreApprovedPiNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OffshoreApprovedPiDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentMots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentMots_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentSsmos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ApplicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CostSettledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RefNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSsmos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentSsmos_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentSupplierFullSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SupplierInvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FsDispatchDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FsDispatchedViaId = table.Column<int>(type: "int", nullable: true),
                    FsTrackingNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FsReceivedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSupplierFullSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierFullSets_Couriers_FsDispatchedViaId",
                        column: x => x.FsDispatchedViaId,
                        principalTable: "Couriers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierFullSets_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShipmentSupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    DueAmountUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PaymentExecutedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentExecutedValue = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    PaymentExecutedCurrencyId = table.Column<int>(type: "int", nullable: true),
                    PaymentExecutedUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    DueBalanceUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    Remarks = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPayments_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPayments_Currencies_PaymentExecutedCurrencyId",
                        column: x => x.PaymentExecutedCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPayments_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentAcds_ShipmentId",
                table: "ShipmentAcds",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_CollectionCurrencyId",
                table: "ShipmentBankings",
                column: "CollectionCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_OsDocDispatchedViaId",
                table: "ShipmentBankings",
                column: "OsDocDispatchedViaId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_ReceivingBankId",
                table: "ShipmentBankings",
                column: "ReceivingBankId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_SenderBankId",
                table: "ShipmentBankings",
                column: "SenderBankId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_ShipmentId",
                table: "ShipmentBankings",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_TenorId",
                table: "ShipmentBankings",
                column: "TenorId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDraftDocuments_ShipmentId",
                table: "ShipmentDraftDocuments",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentForwarders_CurrencyId",
                table: "ShipmentForwarders",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentForwarders_ForwarderId",
                table: "ShipmentForwarders",
                column: "ForwarderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentForwarders_ShipmentId",
                table: "ShipmentForwarders",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentMots_ShipmentId",
                table: "ShipmentMots",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSsmos_ShipmentId",
                table: "ShipmentSsmos",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierFullSets_FsDispatchedViaId",
                table: "ShipmentSupplierFullSets",
                column: "FsDispatchedViaId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierFullSets_ShipmentId",
                table: "ShipmentSupplierFullSets",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPayments_CurrencyId",
                table: "ShipmentSupplierPayments",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPayments_PaymentExecutedCurrencyId",
                table: "ShipmentSupplierPayments",
                column: "PaymentExecutedCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPayments_ShipmentId",
                table: "ShipmentSupplierPayments",
                column: "ShipmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentAcds");

            migrationBuilder.DropTable(
                name: "ShipmentBankings");

            migrationBuilder.DropTable(
                name: "ShipmentDraftDocuments");

            migrationBuilder.DropTable(
                name: "ShipmentForwarders");

            migrationBuilder.DropTable(
                name: "ShipmentMots");

            migrationBuilder.DropTable(
                name: "ShipmentSsmos");

            migrationBuilder.DropTable(
                name: "ShipmentSupplierFullSets");

            migrationBuilder.DropTable(
                name: "ShipmentSupplierPayments");
        }
    }
}
