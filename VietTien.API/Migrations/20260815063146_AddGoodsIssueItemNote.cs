using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsIssueItemNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "GoodsIssueItems",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "GoodsIssueItems");
        }
    }
}
