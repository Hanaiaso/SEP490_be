using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPriceUpdateOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PriceLockedAt",
                table: "CartItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductPriceUpdateOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProposalNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScheduledEffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedSalesStaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceUpdateOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrders_Users_AssignedByManagerId",
                        column: x => x.AssignedByManagerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrders_Users_AssignedSalesStaffId",
                        column: x => x.AssignedSalesStaffId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrders_Users_ExecutedByUserId",
                        column: x => x.ExecutedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrders_Users_ProposedByUserId",
                        column: x => x.ProposedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPriceUpdateOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductPriceUpdateOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceUpdateOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrderItems_ProductPriceUpdateOrders_ProductPriceUpdateOrderId",
                        column: x => x.ProductPriceUpdateOrderId,
                        principalTable: "ProductPriceUpdateOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductPriceUpdateOrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrderItems_ProductId",
                table: "ProductPriceUpdateOrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrderItems_ProductPriceUpdateOrderId",
                table: "ProductPriceUpdateOrderItems",
                column: "ProductPriceUpdateOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrders_AssignedByManagerId",
                table: "ProductPriceUpdateOrders",
                column: "AssignedByManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrders_AssignedSalesStaffId",
                table: "ProductPriceUpdateOrders",
                column: "AssignedSalesStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrders_ExecutedByUserId",
                table: "ProductPriceUpdateOrders",
                column: "ExecutedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceUpdateOrders_ProposedByUserId",
                table: "ProductPriceUpdateOrders",
                column: "ProposedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPriceUpdateOrderItems");

            migrationBuilder.DropTable(
                name: "ProductPriceUpdateOrders");

            migrationBuilder.DropColumn(
                name: "PriceLockedAt",
                table: "CartItems");
        }
    }
}
