using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedCompletionDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedCompletionDate",
                table: "ClearanceActualCharges",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannedCompletionDate",
                table: "ClearanceActualCharges");
        }
    }
}
