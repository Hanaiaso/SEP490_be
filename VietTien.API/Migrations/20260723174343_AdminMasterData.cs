using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AdminMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscountTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountTiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    VehicleNumber = table.Column<int>(type: "int", nullable: false),
                    LicensePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Capacity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DiscountTiers",
                columns: new[] { "Id", "Description", "DiscountPercent", "IsActive", "MaxAmount", "MinAmount" },
                values: new object[,]
                {
                    { new Guid("f0000002-0002-4002-a002-000000000001"), "10tr - <31tr: 5%", 0.05m, true, 31000000m, 10000000m },
                    { new Guid("f0000002-0002-4002-a002-000000000002"), "31tr - <51tr: 6%", 0.06m, true, 51000000m, 31000000m },
                    { new Guid("f0000002-0002-4002-a002-000000000003"), "51tr - <71tr: 7%", 0.07m, true, 71000000m, 51000000m },
                    { new Guid("f0000002-0002-4002-a002-000000000004"), "71tr - <100tr: 8%", 0.08m, true, 100000000m, 71000000m }
                });

            migrationBuilder.InsertData(
                table: "Vehicles",
                columns: new[] { "Id", "Capacity", "IsActive", "LicensePlate", "Note", "VehicleNumber" },
                values: new object[,]
                {
                    { new Guid("f0000001-0001-4001-a001-000000000001"), null, true, "51C-000.01", null, 1 },
                    { new Guid("f0000001-0001-4001-a001-000000000002"), null, true, "51C-000.02", null, 2 },
                    { new Guid("f0000001-0001-4001-a001-000000000003"), null, true, "51C-000.03", null, 3 },
                    { new Guid("f0000001-0001-4001-a001-000000000004"), null, true, "51C-000.04", null, 4 },
                    { new Guid("f0000001-0001-4001-a001-000000000005"), null, true, "51C-000.05", null, 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountTiers_MinAmount",
                table: "DiscountTiers",
                column: "MinAmount");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleNumber",
                table: "Vehicles",
                column: "VehicleNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscountTiers");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
