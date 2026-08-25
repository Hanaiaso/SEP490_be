using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarantineLogParentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentQuarantineLogId",
                table: "QuarantineLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuarantineLogs_ParentQuarantineLogId",
                table: "QuarantineLogs",
                column: "ParentQuarantineLogId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuarantineLogs_QuarantineLogs_ParentQuarantineLogId",
                table: "QuarantineLogs",
                column: "ParentQuarantineLogId",
                principalTable: "QuarantineLogs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuarantineLogs_QuarantineLogs_ParentQuarantineLogId",
                table: "QuarantineLogs");

            migrationBuilder.DropIndex(
                name: "IX_QuarantineLogs_ParentQuarantineLogId",
                table: "QuarantineLogs");

            migrationBuilder.DropColumn(
                name: "ParentQuarantineLogId",
                table: "QuarantineLogs");
        }
    }
}
