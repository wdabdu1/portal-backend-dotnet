using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class AddTruckLoading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TruckLoads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TruckId = table.Column<int>(type: "int", nullable: false),
                    DriverId = table.Column<int>(type: "int", nullable: true),
                    LoadDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TruckLoads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TruckLoads_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TruckLoads_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TruckLoadDrops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TruckLoadId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TruckLoadDrops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TruckLoadDrops_TruckLoads_TruckLoadId",
                        column: x => x.TruckLoadId,
                        principalTable: "TruckLoads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TruckLoadDrops_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TruckLoadItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TruckLoadDropId = table.Column<int>(type: "int", nullable: false),
                    WarehouseAllocationId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    InHousePrice = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ParallelMarketPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TruckLoadItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TruckLoadItems_TruckLoadDrops_TruckLoadDropId",
                        column: x => x.TruckLoadDropId,
                        principalTable: "TruckLoadDrops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TruckLoadItems_WarehouseAllocations_WarehouseAllocationId",
                        column: x => x.WarehouseAllocationId,
                        principalTable: "WarehouseAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoadDrops_TruckLoadId",
                table: "TruckLoadDrops",
                column: "TruckLoadId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoadDrops_WarehouseId",
                table: "TruckLoadDrops",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoadItems_TruckLoadDropId",
                table: "TruckLoadItems",
                column: "TruckLoadDropId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoadItems_WarehouseAllocationId",
                table: "TruckLoadItems",
                column: "WarehouseAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_DriverId",
                table: "TruckLoads",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLoads_TruckId",
                table: "TruckLoads",
                column: "TruckId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TruckLoadItems");

            migrationBuilder.DropTable(
                name: "TruckLoadDrops");

            migrationBuilder.DropTable(
                name: "TruckLoads");
        }
    }
}
