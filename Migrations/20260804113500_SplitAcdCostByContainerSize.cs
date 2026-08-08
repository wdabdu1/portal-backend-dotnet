using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class SplitAcdCostByContainerSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostPerFclUsd",
                table: "AcdCostSettings",
                newName: "Rate40Usd");

            migrationBuilder.AddColumn<decimal>(
                name: "Rate20Usd",
                table: "AcdCostSettings",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rate20Usd",
                table: "AcdCostSettings");

            migrationBuilder.RenameColumn(
                name: "Rate40Usd",
                table: "AcdCostSettings",
                newName: "CostPerFclUsd");
        }
    }
}
