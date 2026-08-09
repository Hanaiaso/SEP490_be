using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsIssueExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "GoodsIssues",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRecipientName",
                table: "GoodsIssues",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "GoodsIssues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaperDocumentNumber",
                table: "GoodsIssues",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "GoodsIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalForIssueId",
                table: "GoodsIssues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "GoodsIssues",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsagePurpose",
                table: "GoodsIssues",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsIssues_PaperDocumentNumber",
                table: "GoodsIssues",
                column: "PaperDocumentNumber",
                unique: true,
                filter: "[PaperDocumentNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoodsIssues_PaperDocumentNumber",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "ExternalRecipientName",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "PaperDocumentNumber",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "ReversalForIssueId",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "UsagePurpose",
                table: "GoodsIssues");
        }
    }
}
