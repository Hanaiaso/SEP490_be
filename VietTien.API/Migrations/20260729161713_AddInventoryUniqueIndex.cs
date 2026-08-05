using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Inventories_MaterialId",
                table: "Inventories");

            migrationBuilder.DropIndex(
                name: "IX_Inventories_ProductId",
                table: "Inventories");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_MaterialId_WarehouseLocationId",
                table: "Inventories",
                columns: new[] { "MaterialId", "WarehouseLocationId" },
                unique: true,
                filter: "[MaterialId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_ProductId_WarehouseLocationId",
                table: "Inventories",
                columns: new[] { "ProductId", "WarehouseLocationId" },
                unique: true,
                filter: "[ProductId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Inventories_MaterialId_WarehouseLocationId",
                table: "Inventories");

            migrationBuilder.DropIndex(
                name: "IX_Inventories_ProductId_WarehouseLocationId",
                table: "Inventories");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_MaterialId",
                table: "Inventories",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_ProductId",
                table: "Inventories",
                column: "ProductId");
        }
    }
}
