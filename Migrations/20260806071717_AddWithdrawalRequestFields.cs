using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "WithdrawalRequestDate",
                table: "Clearances",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalRequestRefNo",
                table: "Clearances",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WithdrawalRequestDate",
                table: "Clearances");

            migrationBuilder.DropColumn(
                name: "WithdrawalRequestRefNo",
                table: "Clearances");
        }
    }
}
