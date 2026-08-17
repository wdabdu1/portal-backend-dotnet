using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddCbosAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AddCbosAllowanceId",
                table: "ShipmentBankings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentBankings_AddCbosAllowanceId",
                table: "ShipmentBankings",
                column: "AddCbosAllowanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Tenors_AddCbosAllowanceId",
                table: "ShipmentBankings",
                column: "AddCbosAllowanceId",
                principalTable: "Tenors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Tenors_AddCbosAllowanceId",
                table: "ShipmentBankings");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentBankings_AddCbosAllowanceId",
                table: "ShipmentBankings");

            migrationBuilder.DropColumn(
                name: "AddCbosAllowanceId",
                table: "ShipmentBankings");
        }
    }
}
