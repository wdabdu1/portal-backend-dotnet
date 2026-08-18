using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPaidToClearanceEstimateLineItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "ClearanceEstimateLineItems",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidDate",
                table: "ClearanceEstimateLineItems",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "ClearanceEstimateLineItems");

            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "ClearanceEstimateLineItems");
        }
    }
}
