using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddOffshoreMarkupDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OffshoreMarkupDefaults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusinessPartnerId = table.Column<int>(type: "int", nullable: false),
                    DefaultMarkupPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    DefaultCurrencyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffshoreMarkupDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OffshoreMarkupDefaults_BusinessPartners_BusinessPartnerId",
                        column: x => x.BusinessPartnerId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OffshoreMarkupDefaults_Currencies_DefaultCurrencyId",
                        column: x => x.DefaultCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OffshoreMarkupDefaults_BusinessPartnerId",
                table: "OffshoreMarkupDefaults",
                column: "BusinessPartnerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffshoreMarkupDefaults_DefaultCurrencyId",
                table: "OffshoreMarkupDefaults",
                column: "DefaultCurrencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OffshoreMarkupDefaults");
        }
    }
}
