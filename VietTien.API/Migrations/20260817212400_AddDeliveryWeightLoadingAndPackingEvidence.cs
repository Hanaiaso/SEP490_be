using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryWeightLoadingAndPackingEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackedBoxCount",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PackingCompletedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackingEvidenceUrls",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPackedWeightKg",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedArrivalAt",
                table: "DeliveryTrips",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedDepartureAt",
                table: "DeliveryTrips",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("3a369d6a-500b-4e11-b127-494e6c74a72e"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("659870d7-5b15-4496-a4bb-03ab28900170"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("a3c3e6e5-860a-464c-a073-1b847a9db570"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("aa275908-173a-47fb-a2cb-8eb173c934ef"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("e24b1960-21d2-4385-8155-17557c0ce8b9"),
                column: "WeightKg",
                value: null);

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("f0000007-0007-4007-a007-000000000001"),
                column: "WeightKg",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackedBoxCount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackingCompletedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackingEvidenceUrls",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TotalPackedWeightKg",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlannedArrivalAt",
                table: "DeliveryTrips");

            migrationBuilder.DropColumn(
                name: "PlannedDepartureAt",
                table: "DeliveryTrips");
        }
    }
}
