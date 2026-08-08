using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class RestructureClearanceSla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MilestoneKey",
                table: "ClearanceSlaSettings",
                newName: "GroupItem");

            migrationBuilder.RenameColumn(
                name: "Label",
                table: "ClearanceSlaSettings",
                newName: "Division");

            migrationBuilder.AddColumn<int>(
                name: "SequenceOrder",
                table: "ClearanceSlaSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SequenceOrder",
                table: "ClearanceSlaSettings");

            migrationBuilder.RenameColumn(
                name: "GroupItem",
                table: "ClearanceSlaSettings",
                newName: "MilestoneKey");

            migrationBuilder.RenameColumn(
                name: "Division",
                table: "ClearanceSlaSettings",
                newName: "Label");
        }
    }
}
