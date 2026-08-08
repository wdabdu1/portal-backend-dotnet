using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddFzWithdrawalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepositShipmentId",
                table: "ClearanceRoute3Details",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositRefNo",
                table: "ClearanceRoute2Details",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "DestinationId",
                table: "ClearanceRoute2Details",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FzInvoiceNo",
                table: "ClearanceRoute2Details",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClearanceRoute3Withdrawals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClearanceRoute3DetailsId = table.Column<int>(type: "int", nullable: false),
                    DepositShipmentLineItemId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceRoute3Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClearanceRoute3Withdrawals_ClearanceRoute3Details_ClearanceR~",
                        column: x => x.ClearanceRoute3DetailsId,
                        principalTable: "ClearanceRoute3Details",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClearanceRoute3Withdrawals_ShipmentLineItems_DepositShipment~",
                        column: x => x.DepositShipmentLineItemId,
                        principalTable: "ShipmentLineItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute3Details_DepositShipmentId",
                table: "ClearanceRoute3Details",
                column: "DepositShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute2Details_DestinationId",
                table: "ClearanceRoute2Details",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute3Withdrawals_ClearanceRoute3DetailsId",
                table: "ClearanceRoute3Withdrawals",
                column: "ClearanceRoute3DetailsId");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceRoute3Withdrawals_DepositShipmentLineItemId",
                table: "ClearanceRoute3Withdrawals",
                column: "DepositShipmentLineItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClearanceRoute2Details_ShipmentDestinations_DestinationId",
                table: "ClearanceRoute2Details",
                column: "DestinationId",
                principalTable: "ShipmentDestinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClearanceRoute3Details_Shipments_DepositShipmentId",
                table: "ClearanceRoute3Details",
                column: "DepositShipmentId",
                principalTable: "Shipments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClearanceRoute2Details_ShipmentDestinations_DestinationId",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropForeignKey(
                name: "FK_ClearanceRoute3Details_Shipments_DepositShipmentId",
                table: "ClearanceRoute3Details");

            migrationBuilder.DropTable(
                name: "ClearanceRoute3Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_ClearanceRoute3Details_DepositShipmentId",
                table: "ClearanceRoute3Details");

            migrationBuilder.DropIndex(
                name: "IX_ClearanceRoute2Details_DestinationId",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropColumn(
                name: "DepositShipmentId",
                table: "ClearanceRoute3Details");

            migrationBuilder.DropColumn(
                name: "DepositRefNo",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "ClearanceRoute2Details");

            migrationBuilder.DropColumn(
                name: "FzInvoiceNo",
                table: "ClearanceRoute2Details");
        }
    }
}
