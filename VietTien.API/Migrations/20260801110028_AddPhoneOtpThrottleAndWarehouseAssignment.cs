using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneOtpThrottleAndWarehouseAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedWarehouseId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhoneOtpFailedAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PhoneOtpSendCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneOtpWindowStart",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("77777777-7777-7777-7777-777777777777"),
                columns: new[] { "AssignedWarehouseId", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart" },
                values: new object[] { null, 0, 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedWarehouseId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneOtpFailedAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneOtpSendCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneOtpWindowStart",
                table: "Users");
        }
    }
}
