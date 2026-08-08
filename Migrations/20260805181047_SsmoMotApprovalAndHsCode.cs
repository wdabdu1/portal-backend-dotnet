using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class SsmoMotApprovalAndHsCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OffshoreApprovedPiDate",
                table: "ShipmentMots",
                newName: "ApprovalDate");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ApprovalDate",
                table: "ShipmentSsmos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsCode",
                table: "ShipmentLineItems",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalDate",
                table: "ShipmentSsmos");

            migrationBuilder.DropColumn(
                name: "HsCode",
                table: "ShipmentLineItems");

            migrationBuilder.RenameColumn(
                name: "ApprovalDate",
                table: "ShipmentMots",
                newName: "OffshoreApprovedPiDate");
        }
    }
}
