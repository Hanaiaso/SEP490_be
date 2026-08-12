using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryUnblockAndDebtSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnblockReason",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnblockedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnblockedByUserId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            // Backfill bằng GETUTCDATE() thay vì hằng số năm 0001: các công nợ tạo trước migration này
            // (nếu có) cần CreatedAt hợp lý để OverdueDays tính ra không bị âm hàng nghìn năm.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "CustomerDebts",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAt",
                table: "CustomerDebts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SettledByUserId",
                table: "CustomerDebts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementNote",
                table: "CustomerDebts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UnblockedByUserId",
                table: "Orders",
                column: "UnblockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebts_SettledByUserId",
                table: "CustomerDebts",
                column: "SettledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDebts_Users_SettledByUserId",
                table: "CustomerDebts",
                column: "SettledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Users_UnblockedByUserId",
                table: "Orders",
                column: "UnblockedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerDebts_Users_SettledByUserId",
                table: "CustomerDebts");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Users_UnblockedByUserId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UnblockedByUserId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDebts_SettledByUserId",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "UnblockReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UnblockedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UnblockedByUserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "SettledByUserId",
                table: "CustomerDebts");

            migrationBuilder.DropColumn(
                name: "SettlementNote",
                table: "CustomerDebts");
        }
    }
}
