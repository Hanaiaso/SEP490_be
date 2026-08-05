using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpRateLimitFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailOtpDayWindowStart",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmailOtpSendCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmailOtpSendCountDaily",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailOtpWindowStart",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("77777777-7777-7777-7777-777777777777"),
                columns: new[] { "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart" },
                values: new object[] { null, 0, 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailOtpDayWindowStart",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailOtpSendCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailOtpSendCountDaily",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailOtpWindowStart",
                table: "Users");
        }
    }
}
