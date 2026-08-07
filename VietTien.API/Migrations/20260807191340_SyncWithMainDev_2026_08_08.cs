using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VietTien.API.Migrations
{
    /// <inheritdoc />
    public partial class SyncWithMainDev_2026_08_08 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryShift",
                table: "StockTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryVehicleId",
                table: "StockTransfers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDeliveryDate",
                table: "StockTransfers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StockTransactions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddress",
                table: "ReturnExchangeRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AverageRating",
                table: "Products",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplyText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RepliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RepliedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductReviews_CustomerProfiles_CustomerProfileId",
                        column: x => x.CustomerProfileId,
                        principalTable: "CustomerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductReviews_Users_RepliedByUserId",
                        column: x => x.RepliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Materials",
                columns: new[] { "Id", "CurrentStock", "LastAlertSentDate", "Name", "SafetyThreshold", "Unit" },
                values: new object[,]
                {
                    { new Guid("f0000005-0005-4005-a005-000000000001"), 0.0, null, "Hạt Nhựa PE Nguyên Sinh", 100.0, "Kg" },
                    { new Guid("f0000005-0005-4005-a005-000000000002"), 0.0, null, "Cuộn Màng PE Thô (Chưa Cắt)", 50.0, "Cuộn" }
                });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("3a369d6a-500b-4e11-b127-494e6c74a72e"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("659870d7-5b15-4496-a4bb-03ab28900170"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("a3c3e6e5-860a-464c-a073-1b847a9db570"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("aa275908-173a-47fb-a2cb-8eb173c934ef"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("e24b1960-21d2-4385-8155-17557c0ce8b9"),
                columns: new[] { "AverageRating", "ReviewCount" },
                values: new object[] { 0.0, 0 });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "AverageRating", "CategoryId", "Description", "ImageUrl", "IsDiscontinued", "Name", "ReviewCount", "Sku", "Specifications", "StandardListedPrice", "Unit" },
                values: new object[] { new Guid("f0000007-0007-4007-a007-000000000001"), 0.0, new Guid("d373bbfa-184c-4eac-9633-38bee5ef6478"), "Băng keo in logo theo yêu cầu, nhập khẩu từ nhà cung cấp đối tác, chất lượng cao cấp cho khách hàng doanh nghiệp.", "https://placehold.co/600x600/f3f4f6/9ca3af?text=Tape+Import", false, "Băng Keo In Logo Nhập Khẩu 5F 100 Yard (Cây 6 Cuộn)", 0, "TAPE-IMP-LOGO5F", "Quy Cách: Cây 6 cuộn\nChiều Rộng: 5cm (5F)\nChiều Dài: 100 Yard\nNguồn Gốc: Nhập khẩu", 95000m, "Cái" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "AssignedWarehouseId",
                value: new Guid("ee73f2cc-05fd-4b0e-8a48-61f89a2d345a"));

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "AssignedWarehouseId", "AvatarUrl", "CreatedAt", "Email", "EmailOtpDayWindowStart", "EmailOtpSendCount", "EmailOtpSendCountDaily", "EmailOtpWindowStart", "FullName", "GoogleId", "IsActive", "IsEmailVerified", "IsPhoneVerified", "OtpCode", "OtpExpiry", "PasswordHash", "PasswordResetToken", "PasswordResetTokenExpiry", "PhoneNumber", "PhoneOtpCode", "PhoneOtpExpiry", "PhoneOtpFailedAttempts", "PhoneOtpSendCount", "PhoneOtpWindowStart", "ReferralCode", "ReferredBySalesStaffId", "RefreshToken", "RefreshTokenExpiryTime", "Role" },
                values: new object[,]
                {
                    { new Guid("44444444-4444-4444-4444-444444444402"), null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "salesstaff2.test@viettien.com", null, 0, 0, null, "Sales Staff Test 2", null, true, true, true, null, null, "$2a$11$yxVoqFJ39C6xv9yAy6v8culp85Msmy.BhBGfAreZWDxCY5RSs0wY.", null, null, "0999000104", null, null, 0, 0, null, null, null, null, null, 2 },
                    { new Guid("44444444-4444-4444-4444-444444444403"), null, null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "salesstaff3.test@viettien.com", null, 0, 0, null, "Sales Staff Test 3", null, true, true, true, null, null, "$2a$11$yxVoqFJ39C6xv9yAy6v8culp85Msmy.BhBGfAreZWDxCY5RSs0wY.", null, null, "0999000204", null, null, 0, 0, null, null, null, null, null, 2 },
                    { new Guid("55555555-5555-5555-5555-555555555502"), new Guid("f0000003-0003-4003-a003-000000000001"), null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "warehousestaff2.test@viettien.com", null, 0, 0, null, "Warehouse Staff Test 2", null, true, true, true, null, null, "$2a$11$yxVoqFJ39C6xv9yAy6v8culp85Msmy.BhBGfAreZWDxCY5RSs0wY.", null, null, "0999000105", null, null, 0, 0, null, null, null, null, null, 4 },
                    { new Guid("55555555-5555-5555-5555-555555555503"), new Guid("f0000004-0004-4004-a004-000000000001"), null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "warehousestaff3.test@viettien.com", null, 0, 0, null, "Warehouse Staff Test 3", null, true, true, true, null, null, "$2a$11$yxVoqFJ39C6xv9yAy6v8culp85Msmy.BhBGfAreZWDxCY5RSs0wY.", null, null, "0999000205", null, null, 0, 0, null, null, null, null, null, 4 }
                });

            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("f0000003-0003-4003-a003-000000000001"), "WH-TRADE", "Kho Thương Mại" },
                    { new Guid("f0000004-0004-4004-a004-000000000001"), "WH-PE", "Kho Màng PE & Xốp" }
                });

            migrationBuilder.InsertData(
                table: "WarehouseLocations",
                columns: new[] { "Id", "Name", "Type", "WarehouseId" },
                values: new object[,]
                {
                    { new Guid("f0000003-0003-4003-a003-000000000002"), "Vị trí mặc định", "Normal", new Guid("f0000003-0003-4003-a003-000000000001") },
                    { new Guid("f0000004-0004-4004-a004-000000000002"), "Vị trí mặc định", "Normal", new Guid("f0000004-0004-4004-a004-000000000001") }
                });

            migrationBuilder.InsertData(
                table: "Inventories",
                columns: new[] { "Id", "AllocatedQuantity", "DamagedQuantity", "InTransitQuantity", "LastUpdatedAt", "LastUpdatedByUserId", "MaterialId", "OnHandQuantity", "ProductId", "QuarantineQuantity", "ReorderThreshold", "ReservedQuantity", "WarehouseLocationId" },
                values: new object[,]
                {
                    { new Guid("f0000008-0008-4008-a008-000000000001"), 0, 0, 0, null, null, null, 8000, new Guid("e24b1960-21d2-4385-8155-17557c0ce8b9"), 0, null, 0, new Guid("f0000004-0004-4004-a004-000000000002") },
                    { new Guid("f0000008-0008-4008-a008-000000000002"), 0, 0, 0, null, null, null, 8000, new Guid("a3c3e6e5-860a-464c-a073-1b847a9db570"), 0, null, 0, new Guid("f0000004-0004-4004-a004-000000000002") },
                    { new Guid("f0000008-0008-4008-a008-000000000003"), 0, 0, 0, null, null, new Guid("f0000005-0005-4005-a005-000000000001"), 500, null, 0, null, 0, new Guid("f0000004-0004-4004-a004-000000000002") },
                    { new Guid("f0000008-0008-4008-a008-000000000004"), 0, 0, 0, null, null, new Guid("f0000005-0005-4005-a005-000000000002"), 300, null, 0, null, 0, new Guid("f0000004-0004-4004-a004-000000000002") },
                    { new Guid("f0000009-0009-4009-a009-000000000001"), 0, 0, 0, null, null, null, 5000, new Guid("659870d7-5b15-4496-a4bb-03ab28900170"), 0, null, 0, new Guid("f0000003-0003-4003-a003-000000000002") },
                    { new Guid("f0000009-0009-4009-a009-000000000002"), 0, 0, 0, null, null, null, 6000, new Guid("cc25fd5c-3ad6-4f95-b19f-e86635d1d16d"), 0, null, 0, new Guid("f0000003-0003-4003-a003-000000000002") },
                    { new Guid("f0000009-0009-4009-a009-000000000003"), 0, 0, 0, null, null, null, 3000, new Guid("aa275908-173a-47fb-a2cb-8eb173c934ef"), 0, null, 0, new Guid("f0000003-0003-4003-a003-000000000002") },
                    { new Guid("f0000009-0009-4009-a009-000000000004"), 0, 0, 0, null, null, null, 2000, new Guid("f0000007-0007-4007-a007-000000000001"), 0, null, 0, new Guid("f0000003-0003-4003-a003-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_CustomerProfileId_ProductId",
                table: "ProductReviews",
                columns: new[] { "CustomerProfileId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_OrderId",
                table: "ProductReviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_ProductId",
                table: "ProductReviews",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_RepliedByUserId",
                table: "ProductReviews",
                column: "RepliedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductReviews");

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000001"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000002"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000003"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000008-0008-4008-a008-000000000004"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000001"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000002"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000003"));

            migrationBuilder.DeleteData(
                table: "Inventories",
                keyColumn: "Id",
                keyValue: new Guid("f0000009-0009-4009-a009-000000000004"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444402"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444403"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555502"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555503"));

            migrationBuilder.DeleteData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: new Guid("f0000005-0005-4005-a005-000000000001"));

            migrationBuilder.DeleteData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: new Guid("f0000005-0005-4005-a005-000000000002"));

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: new Guid("f0000007-0007-4007-a007-000000000001"));

            migrationBuilder.DeleteData(
                table: "WarehouseLocations",
                keyColumn: "Id",
                keyValue: new Guid("f0000003-0003-4003-a003-000000000002"));

            migrationBuilder.DeleteData(
                table: "WarehouseLocations",
                keyColumn: "Id",
                keyValue: new Guid("f0000004-0004-4004-a004-000000000002"));

            migrationBuilder.DeleteData(
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: new Guid("f0000003-0003-4003-a003-000000000001"));

            migrationBuilder.DeleteData(
                table: "Warehouses",
                keyColumn: "Id",
                keyValue: new Guid("f0000004-0004-4004-a004-000000000001"));

            migrationBuilder.DropColumn(
                name: "DeliveryShift",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "DeliveryVehicleId",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ScheduledDeliveryDate",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "PickupAddress",
                table: "ReturnExchangeRequests");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingAddress",
                table: "Orders");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "AssignedWarehouseId",
                value: null);
        }
    }
}
