using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddTruckCurrentCityColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentCityId",
                table: "Trucks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_CurrentCityId",
                table: "Trucks",
                column: "CurrentCityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trucks_LogisticsCities_CurrentCityId",
                table: "Trucks",
                column: "CurrentCityId",
                principalTable: "LogisticsCities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trucks_LogisticsCities_CurrentCityId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_CurrentCityId",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "CurrentCityId",
                table: "Trucks");
        }
    }
}