using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAndBankDuesRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_ShipmentSupplierPayments_Ship~",
                table: "ShipmentSupplierPaymentRecords");

            migrationBuilder.DropTable(
                name: "ShipmentSupplierPayments");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Tenors");

            migrationBuilder.DropColumn(
                name: "CollectionAmountSettled",
                table: "ShipmentBankings");

            migrationBuilder.DropColumn(
                name: "CollectionDueDate",
                table: "ShipmentBankings");

            migrationBuilder.DropColumn(
                name: "RemainingDues",
                table: "ShipmentBankings");

            migrationBuilder.RenameColumn(
                name: "ShipmentSupplierPaymentId",
                table: "ShipmentSupplierPaymentRecords",
                newName: "ShipmentId");

            migrationBuilder.RenameIndex(
                name: "IX_ShipmentSupplierPaymentRecords_ShipmentSupplierPaymentId",
                table: "ShipmentSupplierPaymentRecords",
                newName: "IX_ShipmentSupplierPaymentRecords_ShipmentId");

            migrationBuilder.AddColumn<int>(
                name: "Days",
                table: "Tenors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ShipmentCollectionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ValueUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCollectionRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentCollectionRecords_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentCollectionRecords_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCollectionRecords_CurrencyId",
                table: "ShipmentCollectionRecords",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCollectionRecords_ShipmentId",
                table: "ShipmentCollectionRecords",
                column: "ShipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_Shipments_ShipmentId",
                table: "ShipmentSupplierPaymentRecords",
                column: "ShipmentId",
                principalTable: "Shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_Shipments_ShipmentId",
                table: "ShipmentSupplierPaymentRecords");

            migrationBuilder.DropTable(
                name: "ShipmentCollectionRecords");

            migrationBuilder.DropColumn(
                name: "Days",
                table: "Tenors");

            migrationBuilder.RenameColumn(
                name: "ShipmentId",
                table: "ShipmentSupplierPaymentRecords",
                newName: "ShipmentSupplierPaymentId");

            migrationBuilder.RenameIndex(
                name: "IX_ShipmentSupplierPaymentRecords_ShipmentId",
                table: "ShipmentSupplierPaymentRecords",
                newName: "IX_ShipmentSupplierPaymentRecords_ShipmentSupplierPaymentId");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Tenors",
                type: "varchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "CollectionAmountSettled",
                table: "ShipmentBankings",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CollectionDueDate",
                table: "ShipmentBankings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingDues",
                table: "ShipmentBankings",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShipmentSupplierPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InvoiceCurrencyId = table.Column<int>(type: "int", nullable: true),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    BalanceUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    InvoiceValue = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    InvoiceValueUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    Remarks = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SupplierInvoiceNo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalPaidUsd = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentSupplierPayments_Currencies_InvoiceCurrencyId",
                        column: x => x.InvoiceCurrencyId,
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
                name: "IX_ShipmentSupplierPayments_InvoiceCurrencyId",
                table: "ShipmentSupplierPayments",
                column: "InvoiceCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSupplierPayments_ShipmentId",
                table: "ShipmentSupplierPayments",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierPaymentRecords_ShipmentSupplierPayments_Ship~",
                table: "ShipmentSupplierPaymentRecords",
                column: "ShipmentSupplierPaymentId",
                principalTable: "ShipmentSupplierPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
