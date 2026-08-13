using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCountingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryCountingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessions_Users_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessions_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCountingSessionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SystemQuantity = table.Column<int>(type: "int", nullable: false),
                    PhysicalQuantity = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AutoApplied = table.Column<bool>(type: "bit", nullable: false),
                    StockAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountingSessionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessionItems_Inventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "Inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessionItems_InventoryCountingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "InventoryCountingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryCountingSessionItems_StockAdjustments_StockAdjustmentId",
                        column: x => x.StockAdjustmentId,
                        principalTable: "StockAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SystemConfigs",
                columns: new[] { "Key", "Description", "IsActive", "OwnerLevel", "Unit", "ValueType" },
                values: new object[] { "INVENTORY_COUNT_VARIANCE_THRESHOLD", "Chênh lệch tối đa (số lượng tuyệt đối) khi đóng phiên kiểm kê được áp dụng thẳng; vượt ngưỡng bắt buộc CEO duyệt", true, "Admin/CEO", "Đơn vị", "Int" });

            migrationBuilder.InsertData(
                table: "SystemConfigVersions",
                columns: new[] { "Id", "ActorEmail", "ActorUserId", "ChangeReason", "ConfigKey", "CreatedAt", "EffectiveDate", "Value" },
                values: new object[] { new Guid("a0000001-0001-4001-a001-000000000013"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "INVENTORY_COUNT_VARIANCE_THRESHOLD", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessionItems_InventoryId",
                table: "InventoryCountingSessionItems",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessionItems_SessionId",
                table: "InventoryCountingSessionItems",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessionItems_StockAdjustmentId",
                table: "InventoryCountingSessionItems",
                column: "StockAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessions_ClosedByUserId",
                table: "InventoryCountingSessions",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessions_OpenedByUserId",
                table: "InventoryCountingSessions",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountingSessions_WarehouseId",
                table: "InventoryCountingSessions",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryCountingSessionItems");

            migrationBuilder.DropTable(
                name: "InventoryCountingSessions");

            migrationBuilder.DeleteData(
                table: "SystemConfigVersions",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0001-4001-a001-000000000013"));

            migrationBuilder.DeleteData(
                table: "SystemConfigs",
                keyColumn: "Key",
                keyValue: "INVENTORY_COUNT_VARIANCE_THRESHOLD");
        }
    }
}
