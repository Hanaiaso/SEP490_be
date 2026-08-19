using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSlowMovingDaysThresholdConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemConfigs",
                columns: new[] { "Key", "Description", "IsActive", "OwnerLevel", "Unit", "ValueType" },
                values: new object[] { "SLOW_MOVING_DAYS_THRESHOLD", "Số ngày không phát sinh xuất kho để tính là hàng chậm luân chuyển / tồn đọng (dùng chung cho cảnh báo chủ động và KPI Dashboard Kho)", true, "Admin/CEO", "Ngày", "Int" });

            migrationBuilder.InsertData(
                table: "SystemConfigVersions",
                columns: new[] { "Id", "ActorEmail", "ActorUserId", "ChangeReason", "ConfigKey", "CreatedAt", "EffectiveDate", "Value" },
                values: new object[] { new Guid("a0000001-0001-4001-a001-000000000014"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "SLOW_MOVING_DAYS_THRESHOLD", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "30" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemConfigVersions",
                keyColumn: "Id",
                keyValue: new Guid("a0000001-0001-4001-a001-000000000014"));

            migrationBuilder.DeleteData(
                table: "SystemConfigs",
                keyColumn: "Key",
                keyValue: "SLOW_MOVING_DAYS_THRESHOLD");
        }
    }
}
