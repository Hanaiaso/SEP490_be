using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryThresholdsAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExcessThreshold",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAlertSentDate",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReorderThreshold",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxStockThreshold",
                table: "Materials",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSlowMovingAlertSentAt",
                table: "Inventories",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("0f79f912-5b0d-4d7c-ad7b-35fbf4a6497e"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("16eaf448-d4e7-4757-b60c-3a3348cbf10c"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("9925c3cf-e4af-4a88-8840-961d4281f417"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("b115bc37-ab72-40e4-b1fa-274d7b329efe"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("c9e0fca9-3d26-402b-b6a4-58357e527d10"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("d934b287-a9e9-4d7c-86cf-4e82d97957f6"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000001"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000002"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000003"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000004"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000001"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000002"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000003"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000004"),
                column: "LastSlowMovingAlertSentAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: new Guid("f0000005-0005-4005-a005-000000000001"),
                column: "MaxStockThreshold",
                value: null);

            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: new Guid("f0000005-0005-4005-a005-000000000002"),
                column: "MaxStockThreshold",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("3a369d6a-500b-4e11-b127-494e6c74a72e"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("659870d7-5b15-4496-a4bb-03ab28900170"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("a3c3e6e5-860a-464c-a073-1b847a9db570"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("aa275908-173a-47fb-a2cb-8eb173c934ef"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("e24b1960-21d2-4385-8155-17557c0ce8b9"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("f0000007-0007-4007-a007-000000000001"),
                columns: new[] { "ExcessThreshold", "LastAlertSentDate", "ReorderThreshold" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcessThreshold",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastAlertSentDate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReorderThreshold",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MaxStockThreshold",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "LastSlowMovingAlertSentAt",
                table: "Inventories");
        }
    }
}
