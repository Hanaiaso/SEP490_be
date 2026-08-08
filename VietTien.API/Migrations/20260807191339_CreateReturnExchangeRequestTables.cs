using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class CreateReturnExchangeRequestTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnExchangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceUrls = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickupAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PickupShift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PickupStatus = table.Column<int>(type: "int", nullable: false),
                    PickupVehicleId = table.Column<int>(type: "int", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplacementOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledPickupDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnExchangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequests_CustomerProfiles_CustomerProfileId",
                        column: x => x.CustomerProfileId,
                        principalTable: "CustomerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequests_Orders_ReplacementOrderId",
                        column: x => x.ReplacementOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequests_Users_ProcessedByUserId",
                        column: x => x.ProcessedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnExchangeRequestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReturnExchangeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnExchangeRequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequestItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequestItems_ReturnExchangeRequests_ReturnExchangeRequestId",
                        column: x => x.ReturnExchangeRequestId,
                        principalTable: "ReturnExchangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReturnExchangeRequestNewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReturnExchangeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnExchangeRequestNewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequestNewItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnExchangeRequestNewItems_ReturnExchangeRequests_ReturnExchangeRequestId",
                        column: x => x.ReturnExchangeRequestId,
                        principalTable: "ReturnExchangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequests_CustomerProfileId",
                table: "ReturnExchangeRequests",
                column: "CustomerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequests_OrderId",
                table: "ReturnExchangeRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequests_ProcessedByUserId",
                table: "ReturnExchangeRequests",
                column: "ProcessedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequests_ReplacementOrderId",
                table: "ReturnExchangeRequests",
                column: "ReplacementOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequests_RequestedByUserId",
                table: "ReturnExchangeRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequestItems_ProductId",
                table: "ReturnExchangeRequestItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequestItems_ReturnExchangeRequestId",
                table: "ReturnExchangeRequestItems",
                column: "ReturnExchangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequestNewItems_ProductId",
                table: "ReturnExchangeRequestNewItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnExchangeRequestNewItems_ReturnExchangeRequestId",
                table: "ReturnExchangeRequestNewItems",
                column: "ReturnExchangeRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnExchangeRequestItems");

            migrationBuilder.DropTable(
                name: "ReturnExchangeRequestNewItems");

            migrationBuilder.DropTable(
                name: "ReturnExchangeRequests");
        }
    }
}
