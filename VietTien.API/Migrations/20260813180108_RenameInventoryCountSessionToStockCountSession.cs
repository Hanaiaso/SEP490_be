using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameInventoryCountSessionToStockCountSession : Migration
    {
        // EF scaffold mặc định coi đổi tên class C# (InventoryCountSession -> StockCountSession) là
        // drop bảng cũ + tạo bảng mới, sẽ XÓA SẠCH data hiện có (kể cả data thật đã ghi trên production
        // lúc apply migration trước) — viết tay lại bằng RenameTable/RenameColumn/sp_rename để giữ nguyên
        // data, chỉ đổi tên bảng/cột/constraint/index.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "InventoryCountLines", newName: "StockCountLines");
            migrationBuilder.RenameTable(name: "InventoryCountSessions", newName: "StockCountSessions");

            migrationBuilder.RenameColumn(name: "InventoryCountSessionId", table: "StockCountLines", newName: "StockCountSessionId");

            migrationBuilder.RenameIndex(table: "StockCountLines", name: "IX_InventoryCountLines_InventoryCountSessionId_InventoryId", newName: "IX_StockCountLines_StockCountSessionId_InventoryId");
            migrationBuilder.RenameIndex(table: "StockCountLines", name: "IX_InventoryCountLines_InventoryId", newName: "IX_StockCountLines_InventoryId");
            migrationBuilder.RenameIndex(table: "StockCountSessions", name: "IX_InventoryCountSessions_CreatedByUserId", newName: "IX_StockCountSessions_CreatedByUserId");
            migrationBuilder.RenameIndex(table: "StockCountSessions", name: "IX_InventoryCountSessions_WarehouseId_Status", newName: "IX_StockCountSessions_WarehouseId_Status");

            migrationBuilder.Sql("EXEC sp_rename N'dbo.PK_InventoryCountSessions', N'PK_StockCountSessions', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.PK_InventoryCountLines', N'PK_StockCountLines', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_InventoryCountSessions_Users_CreatedByUserId', N'FK_StockCountSessions_Users_CreatedByUserId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_InventoryCountSessions_Warehouses_WarehouseId', N'FK_StockCountSessions_Warehouses_WarehouseId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_InventoryCountLines_Inventories_InventoryId', N'FK_StockCountLines_Inventories_InventoryId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_InventoryCountLines_InventoryCountSessions_InventoryCountSessionId', N'FK_StockCountLines_StockCountSessions_StockCountSessionId', N'OBJECT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_StockCountLines_StockCountSessions_StockCountSessionId', N'FK_InventoryCountLines_InventoryCountSessions_InventoryCountSessionId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_StockCountLines_Inventories_InventoryId', N'FK_InventoryCountLines_Inventories_InventoryId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_StockCountSessions_Warehouses_WarehouseId', N'FK_InventoryCountSessions_Warehouses_WarehouseId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.FK_StockCountSessions_Users_CreatedByUserId', N'FK_InventoryCountSessions_Users_CreatedByUserId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.PK_StockCountLines', N'PK_InventoryCountLines', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'dbo.PK_StockCountSessions', N'PK_InventoryCountSessions', N'OBJECT';");

            migrationBuilder.RenameIndex(table: "StockCountSessions", name: "IX_StockCountSessions_WarehouseId_Status", newName: "IX_InventoryCountSessions_WarehouseId_Status");
            migrationBuilder.RenameIndex(table: "StockCountSessions", name: "IX_StockCountSessions_CreatedByUserId", newName: "IX_InventoryCountSessions_CreatedByUserId");
            migrationBuilder.RenameIndex(table: "StockCountLines", name: "IX_StockCountLines_InventoryId", newName: "IX_InventoryCountLines_InventoryId");
            migrationBuilder.RenameIndex(table: "StockCountLines", name: "IX_StockCountLines_StockCountSessionId_InventoryId", newName: "IX_InventoryCountLines_InventoryCountSessionId_InventoryId");

            migrationBuilder.RenameColumn(name: "StockCountSessionId", table: "StockCountLines", newName: "InventoryCountSessionId");

            migrationBuilder.RenameTable(name: "StockCountSessions", newName: "InventoryCountSessions");
            migrationBuilder.RenameTable(name: "StockCountLines", newName: "InventoryCountLines");
        }
    }
}
