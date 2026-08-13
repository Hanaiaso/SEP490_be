using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookMetricsSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMetricsSyncedAt",
                table: "MarketingPosts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FacebookApiTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    AccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacebookApiTokens", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacebookApiTokens");

            migrationBuilder.DropColumn(
                name: "LastMetricsSyncedAt",
                table: "MarketingPosts");
        }
    }
}
