using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class DropStockCountSessionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockCountLines");

            migrationBuilder.DropTable(
                name: "StockCountSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockCountSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TheoreticalLockedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountSessions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountSessions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InventoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActualQuantity = table.Column<int>(type: "int", nullable: true),
                    CountedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TheoreticalQuantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Inventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "Inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCountSessions_StockCountSessionId",
                        column: x => x.StockCountSessionId,
                        principalTable: "StockCountSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_InventoryId",
                table: "StockCountLines",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountSessionId_InventoryId",
                table: "StockCountLines",
                columns: new[] { "StockCountSessionId", "InventoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCountSessions_CreatedByUserId",
                table: "StockCountSessions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountSessions_WarehouseId_Status",
                table: "StockCountSessions",
                columns: new[] { "WarehouseId", "Status" });
        }
    }
}
