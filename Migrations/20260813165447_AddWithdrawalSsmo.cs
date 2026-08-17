using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalSsmo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SsmoApplicationDate",
                table: "Withdrawals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SsmoApprovalDate",
                table: "Withdrawals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SsmoCocAvailable",
                table: "Withdrawals",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SsmoCocRequired",
                table: "Withdrawals",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SsmoCost",
                table: "Withdrawals",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SsmoCostSettledDate",
                table: "Withdrawals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsmoRefNumber",
                table: "Withdrawals",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsmoApplicationDate",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoApprovalDate",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoCocAvailable",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoCocRequired",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoCost",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoCostSettledDate",
                table: "Withdrawals");

            migrationBuilder.DropColumn(
                name: "SsmoRefNumber",
                table: "Withdrawals");
        }
    }
}
