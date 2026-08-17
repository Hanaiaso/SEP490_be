using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCartReservationAndRedInvoiceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedInvoiceDocumentUrl",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RedInvoiceEnteredByUserId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RedInvoiceIssuedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedInvoiceNumber",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservationReleasedAt",
                table: "CartItems",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RedInvoiceDocumentUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RedInvoiceEnteredByUserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RedInvoiceIssuedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RedInvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReservationReleasedAt",
                table: "CartItems");
        }
    }
}
