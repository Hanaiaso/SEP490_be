using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AdminFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigs",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ConfigKey = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemConfigVersions_SystemConfigs_ConfigKey",
                        column: x => x.ConfigKey,
                        principalTable: "SystemConfigs",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SystemConfigs",
                columns: new[] { "Key", "Description", "IsActive", "OwnerLevel", "Unit", "ValueType" },
                values: new object[,]
                {
                    { "COD_ESCALATION_MINUTES", "Mốc leo thang cảnh báo Manager cho đơn COD", true, "Admin", "Phút", "Int" },
                    { "COD_RESERVATION_MINUTES", "Thời gian giữ tồn cho đơn COD chờ xác nhận", true, "Admin", "Phút", "Int" },
                    { "COD_WARNING_MINUTES", "Mốc cảnh báo Sale trước khi hết hạn giữ tồn COD", true, "Admin", "Phút", "Int" },
                    { "DELIVERY_FAILURE_MANAGER_THRESHOLD", "Số lần giao thất bại trước khi báo Manager", true, "Admin/Manager", "Lần thử giao", "Int" },
                    { "LIST_PRICE_MAX_EXCLUSIVE", "Ngưỡng áp dụng giá niêm yết (dưới ngưỡng này)", true, "Admin/CEO", "VND", "Decimal" },
                    { "MAX_SCHEDULED_MARKETING_POSTS", "Số bài viết marketing được lên lịch tối đa", true, "Admin", "Bài viết", "Int" },
                    { "OTP_EXPIRE_MINUTES", "Thời gian hết hạn mã OTP", true, "Admin", "Phút", "Int" },
                    { "OTP_MAX_ATTEMPTS", "Số lần gửi OTP tối đa trong 30 phút", true, "Admin", "Lần", "Int" },
                    { "OTP_RESEND_SECONDS", "Thời gian tối thiểu giữa 2 lần gửi lại OTP", true, "Admin", "Giây", "Int" },
                    { "PRICE_LOCK_HOURS", "Thời gian khóa giá báo giá", true, "Admin", "Giờ", "Int" },
                    { "QUOTATION_MIN_VALUE", "Ngưỡng giá trị đơn bắt buộc chuyển sang luồng báo giá", true, "Admin/CEO", "VND", "Decimal" },
                    { "SEPAY_RESERVATION_MINUTES", "Thời gian giữ tồn cho đơn SePay chờ thanh toán", true, "Admin", "Phút", "Int" }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("77777777-7777-7777-7777-777777777777"),
                column: "IsActive",
                value: true);

            migrationBuilder.InsertData(
                table: "SystemConfigVersions",
                columns: new[] { "Id", "ActorEmail", "ActorUserId", "ChangeReason", "ConfigKey", "CreatedAt", "EffectiveDate", "Value" },
                values: new object[,]
                {
                    { new Guid("a0000001-0001-4001-a001-000000000001"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "PRICE_LOCK_HOURS", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "24" },
                    { new Guid("a0000001-0001-4001-a001-000000000002"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "SEPAY_RESERVATION_MINUTES", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "15" },
                    { new Guid("a0000001-0001-4001-a001-000000000003"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "COD_RESERVATION_MINUTES", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "35" },
                    { new Guid("a0000001-0001-4001-a001-000000000004"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "COD_WARNING_MINUTES", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "25" },
                    { new Guid("a0000001-0001-4001-a001-000000000005"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "COD_ESCALATION_MINUTES", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { new Guid("a0000001-0001-4001-a001-000000000006"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "OTP_EXPIRE_MINUTES", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5" },
                    { new Guid("a0000001-0001-4001-a001-000000000007"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "OTP_RESEND_SECONDS", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "60" },
                    { new Guid("a0000001-0001-4001-a001-000000000008"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "OTP_MAX_ATTEMPTS", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5" },
                    { new Guid("a0000001-0001-4001-a001-000000000009"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "QUOTATION_MIN_VALUE", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "100000000" },
                    { new Guid("a0000001-0001-4001-a001-000000000010"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "LIST_PRICE_MAX_EXCLUSIVE", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "10000000" },
                    { new Guid("a0000001-0001-4001-a001-000000000011"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "MAX_SCHEDULED_MARKETING_POSTS", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { new Guid("a0000001-0001-4001-a001-000000000012"), "system-seed", null, "Khởi tạo giá trị mặc định theo business.md §7", "DELIVERY_FAILURE_MANAGER_THRESHOLD", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "3" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName",
                table: "AuditLogs",
                column: "EntityName");

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfigVersions_ConfigKey_EffectiveDate",
                table: "SystemConfigVersions",
                columns: new[] { "ConfigKey", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "SystemConfigVersions");

            migrationBuilder.DropTable(
                name: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");
        }
    }
}
