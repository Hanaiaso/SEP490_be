using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-OrderFulfilment — checkout từ giỏ → xác nhận → hàng đợi kho → xuất kho.
    /// L2-FUL-08 nằm ở <see cref="L2InventoryContentionTests"/> (đã Pass).
    ///
    /// ⚠ Route thật khác cột When của workbook (R8, đã grep 209 route):
    ///   "POST /api/orders"            -> POST /api/orders/place-order
    ///   "POST /api/orders/{id}/confirm" -> POST /api/orders/sales/{id:guid}/confirm
    /// </summary>
    [Trait("Category", "L2")]
    public class L2OrderFulfilmentTests : SqlServerTestBase
    {
        public L2OrderFulfilmentTests(SqlServerFixture factory) : base(factory) { }

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        private sealed record CartFixture(Guid UserId, Guid ProfileId, Guid ProductId, Guid InventoryId, decimal Total);

        /// <summary>Dựng khách hàng có hồ sơ + giỏ hàng, và đặt tồn kho về mức chỉ định.</summary>
        private async Task<(HttpClient Client, CartFixture Fixture)> SeedCustomerWithCartAsync(
            int stockOnHand, int cartQty, decimal unitPrice, DateTime? cartUpdatedAt = null,
            int itemCount = 1, Guid? assignedSalesStaffId = null)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);

            Guid profileId = Guid.NewGuid();
            Guid productId = Guid.Empty, inventoryId = Guid.Empty;
            decimal total = 0m;

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = stockOnHand;
                inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                productId = inv.ProductId!.Value; inventoryId = inv.Id;

                foreach (var o in await db.Inventories.Where(i => i.ProductId == productId && i.Id != inv.Id).ToListAsync())
                {
                    o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0;
                }

                db.CustomerProfiles.Add(new CustomerProfile
                {
                    Id = profileId,
                    UserId = user.Id,
                    // SYS_02_NewOrder chỉ được gửi khi hồ sơ có Sale phụ trách (OrderService.cs:249).
                    AssignedSalesStaffId = assignedSalesStaffId
                });

                var cart = new Cart
                {
                    Id = Guid.NewGuid(),
                    CustomerProfileId = profileId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = cartUpdatedAt ?? DateTime.UtcNow,
                    Items = new List<CartItem>()
                };
                for (var n = 0; n < itemCount; n++)
                {
                    cart.Items.Add(new CartItem { ProductId = productId, Quantity = cartQty, UnitPrice = unitPrice });
                    total += unitPrice * cartQty;
                }
                db.Carts.Add(cart);
            });

            return (client, new CartFixture(user.Id, profileId, productId, inventoryId, total));
        }

        private Task<Inventory> ReloadInvAsync(Guid id) =>
            QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == id));

        private Task<List<Order>> OrdersOfProfileAsync(Guid profileId) =>
            QueryAsync(db => db.Orders.AsNoTracking().Where(o => o.CustomerProfileId == profileId).ToListAsync());

        // ── L2-FUL-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Khách U1 có giỏ hàng còn hạn giá, sản phẩm còn tồn
        // WHEN   POST /api/orders/place-order
        // THEN   200; Order + OrderItems khớp giỏ; giỏ được dọn; có email hoá đơn + thông báo cho Sales
        [Fact]
        [Trait("TestID", "L2-FUL-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-01 AC-05; FT-03 AC-04; BR-030")]
        public async Task L2_FUL_01_CheckoutCreatesOrderClearsCartAndNotifies()
        {
            await ResetAsync();
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (client, f) = await SeedCustomerWithCartAsync(
                stockOnHand: 100, cartQty: 2, unitPrice: 500_000m, assignedSalesStaffId: sales.Id);

            var response = await client.PostAsJsonAsync("/api/orders/place-order",
                new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.SePay, Notes = "L2-FUL-01" });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB — scope mới
            var orders = await OrdersOfProfileAsync(f.ProfileId);
            orders.Should().ContainSingle("đúng 1 đơn được tạo");
            var order = orders[0];
            order.PaymentMethod.Should().Be(PaymentMethod.SePay);

            var items = await QueryAsync(db => db.OrderItems.AsNoTracking()
                .Where(oi => oi.OrderId == order.Id).ToListAsync());
            items.Should().ContainSingle();
            items[0].ProductId.Should().Be(f.ProductId);
            items[0].Quantity.Should().Be(2, "phải khớp số lượng trong giỏ");
            order.TotalAmount.Should().Be(f.Total, "tổng tiền tính từ giỏ phía server");

            // Giỏ được dọn — cùng transaction với việc tạo đơn
            (await QueryAsync(db => db.CartItems.CountAsync(ci => ci.Cart.CustomerProfileId == f.ProfileId)))
                .Should().Be(0, "giỏ phải được dọn sau khi đặt hàng");

            // Tồn được giữ mềm, không âm
            var inv = await ReloadInvAsync(f.InventoryId);
            inv.ReservedQuantity.Should().Be(2);
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);

            // (c) side effect — thông báo đơn mới cho Sales
            (await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == order.Id && n.Type == NotificationType.SYS_02_NewOrder)))
                .Should().BeGreaterThan(0, "phải thông báo đơn mới cho Sales");
        }

        // ── L2-FUL-02 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Giỏ có P1 qty=5 nhưng tồn P1 đã về 0
        // WHEN   POST /api/orders/place-order
        // THEN   4xx nêu tên sản phẩm; KHÔNG có Order/OrderItem; giỏ còn nguyên; tồn không đổi — rollback trọn vẹn
        [Fact]
        [Trait("TestID", "L2-FUL-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-01 NAC-03; FT-05 NAC-01")]
        public async Task L2_FUL_02_CheckoutWithoutStockRollsBackCompletely()
        {
            await ResetAsync();
            var (client, f) = await SeedCustomerWithCartAsync(stockOnHand: 0, cartQty: 5, unitPrice: 500_000m);

            var response = await client.PostAsJsonAsync("/api/orders/place-order",
                new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.SePay });

            // (a) HTTP
            ((int)response.StatusCode).Should().BeInRange(400, 499,
                "hết tồn phải bị từ chối; body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB — rollback trọn vẹn
            (await OrdersOfProfileAsync(f.ProfileId)).Should().BeEmpty("không được tạo Order nào");
            (await QueryAsync(db => db.OrderItems.CountAsync())).Should().Be(0);
            (await QueryAsync(db => db.CartItems.CountAsync(ci => ci.Cart.CustomerProfileId == f.ProfileId)))
                .Should().Be(1, "giỏ phải còn nguyên khi đặt hàng thất bại");

            var inv = await ReloadInvAsync(f.InventoryId);
            inv.OnHandQuantity.Should().Be(0);
            inv.ReservedQuantity.Should().Be(0, "không được giữ tồn khi đã hết hàng");
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);
        }

        // ── L2-FUL-03 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Giỏ có mốc giữ giá quá 24 giờ (Cart.UpdatedAt = NOW - 24h01m)
        // WHEN   POST /api/orders/place-order
        // THEN   4xx yêu cầu xem lại giá đã làm mới; không tạo đơn
        [Fact]
        [Trait("TestID", "L2-FUL-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-01 NAC-03; BV-01; BR-025")]
        public async Task L2_FUL_03_CheckoutWithExpiredPriceLockIsRejected()
        {
            await ResetAsync();
            var (client, f) = await SeedCustomerWithCartAsync(
                stockOnHand: 100, cartQty: 2, unitPrice: 500_000m,
                cartUpdatedAt: DateTime.UtcNow.AddHours(-24).AddMinutes(-1));

            var response = await client.PostAsJsonAsync("/api/orders/place-order",
                new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.SePay });

            // (a) HTTP — BR-025: giá giữ 24h hết hạn thì phải bắt khách xem lại giá mới
            ((int)response.StatusCode).Should().BeInRange(400, 499,
                "BR-025: giỏ quá hạn giữ giá 24h không được checkout thẳng; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) DB
            (await OrdersOfProfileAsync(f.ProfileId)).Should().BeEmpty("không được tạo đơn với giá đã hết hạn");
            var inv = await ReloadInvAsync(f.InventoryId);
            inv.ReservedQuantity.Should().Be(0);
        }

        // ── L2-FUL-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Tổng thật của giỏ tính phía server; payload cố nhét tổng tiền giả
        // WHEN   POST /api/orders/place-order với body bị can thiệp
        // THEN   BR-005: giá do client gửi KHÔNG được ảnh hưởng tới đơn
        [Fact]
        [Trait("TestID", "L2-FUL-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 NAC-01; BR-005")]
        public async Task L2_FUL_04_ClientSuppliedPriceCannotInfluenceOrderTotal()
        {
            await ResetAsync();
            var (client, f) = await SeedCustomerWithCartAsync(stockOnHand: 100, cartQty: 3, unitPrice: 35_000_000m);
            var trueTotal = f.Total;   // 105.000.000

            // PlaceOrderRequestDto KHÔNG có trường tiền nào — nhét thêm field giả vào JSON thô.
            var tamperedJson = """
            {"paymentMethod":1,"notes":"L2-FUL-04",
             "totalAmount":99999999,"finalPayment":99999999,"discountAmount":99999999}
            """;
            var response = await client.PostAsync("/api/orders/place-order",
                new StringContent(tamperedJson, Encoding.UTF8, "application/json"));

            var body = await response.Content.ReadAsStringAsync();

            // (b) DB là nguồn sự thật: dù request có gì, tổng tiền phải do server tính từ giỏ
            var orders = await OrdersOfProfileAsync(f.ProfileId);
            if (response.IsSuccessStatusCode)
            {
                orders.Should().ContainSingle();
                orders[0].TotalAmount.Should().Be(trueTotal,
                    "BR-005: tổng tiền phải tính từ giỏ phía server, không nhận từ client");
                orders[0].FinalPayment.Should().NotBe(99_999_999m,
                    "BR-005: giá client gửi lên tuyệt đối không được dùng");
            }
            else
            {
                ((int)response.StatusCode).Should().BeInRange(400, 499, "body: {0}", body);
                orders.Should().BeEmpty("bị từ chối thì không được tạo đơn");
            }
        }

        // ── L2-FUL-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn A (đặt trước) và B (đặt sau), cả hai đủ điều kiện xác nhận
        // WHEN   Sales xác nhận A rồi B; kho GET /api/warehouse/orders
        // THEN   Cả hai Confirmed; hàng đợi kho trả A trước B (FIFO)
        [Fact]
        [Trait("TestID", "L2-FUL-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-05 AC-01; BR-011")]
        public async Task L2_FUL_05_ConfirmedOrdersEnterWarehouseQueueInFifoOrder()
        {
            await ResetAsync();
            var (salesClient, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (warehouseClient, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

            Guid orderA = Guid.NewGuid(), orderB = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 100; inv.ReservedQuantity = 10;
                inv.AllocatedQuantity = 0; inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0;
                var productId = inv.ProductId!.Value;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();

                Order Make(Guid id, DateTime createdAt) => new()
                {
                    Id = id, CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 1_000_000m, FinalPayment = 1_000_000m,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Paid,
                    OrderStatus = OrderStatus.PendingConfirmation,
                    CreatedAt = createdAt,
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = productId, Quantity = 1, PriceSnapshot = 1_000_000m, CostSnapshot = 0m }
                    }
                };
                db.Orders.Add(Make(orderA, DateTime.UtcNow.AddMinutes(-10)));   // đặt trước
                db.Orders.Add(Make(orderB, DateTime.UtcNow.AddMinutes(-5)));    // đặt sau
            });

            var confirmA = await salesClient.PostAsync($"/api/orders/sales/{orderA}/confirm", null);
            confirmA.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await confirmA.Content.ReadAsStringAsync());
            var confirmB = await salesClient.PostAsync($"/api/orders/sales/{orderB}/confirm", null);
            confirmB.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await confirmB.Content.ReadAsStringAsync());

            // (b) DB
            var a = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderA));
            var b = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderB));
            a.OrderStatus.Should().Be(OrderStatus.Confirmed);
            b.OrderStatus.Should().Be(OrderStatus.Confirmed);
            a.ConfirmedAt.Should().NotBeNull();
            b.ConfirmedAt.Should().NotBeNull();

            // (a) hàng đợi kho theo FIFO
            // tabType bắt buộc; "OnlinePending" = đơn Confirmed chưa soạn (WarehouseService.cs:34).
            var queue = await warehouseClient.GetAsync("/api/warehouse/orders?tabType=OnlinePending&pageSize=50");
            queue.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await queue.Content.ReadAsStringAsync());

            var queueBody = await queue.Content.ReadAsStringAsync();
            var idxA = queueBody.IndexOf(orderA.ToString(), StringComparison.OrdinalIgnoreCase);
            var idxB = queueBody.IndexOf(orderB.ToString(), StringComparison.OrdinalIgnoreCase);
            idxA.Should().BeGreaterThanOrEqualTo(0, "đơn A phải nằm trong hàng đợi kho");
            idxB.Should().BeGreaterThanOrEqualTo(0, "đơn B phải nằm trong hàng đợi kho");
            idxA.Should().BeLessThan(idxB, "BR-011: FIFO — đơn đặt trước phải đứng trước trong hàng đợi");
        }

        // ── L2-FUL-06 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn C đang ở trạng thái Cancelled
        // WHEN   POST /api/orders/sales/C/confirm
        // THEN   409 "đơn đã đóng"; DB không đổi; không sinh phiếu soạn hàng
        [Fact]
        [Trait("TestID", "L2-FUL-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-05 NAC-05")]
        public async Task L2_FUL_06_ConfirmingCancelledOrderIsRejected()
        {
            await ResetAsync();
            var (salesClient, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var orderId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 100; inv.ReservedQuantity = 0; inv.AllocatedQuantity = 0;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Orders.Add(new Order
                {
                    Id = orderId, CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 1_000_000m, FinalPayment = 1_000_000m,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Paid,
                    OrderStatus = OrderStatus.Cancelled,
                    CancelReason = "Khach huy",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = inv.ProductId!.Value, Quantity = 1, PriceSnapshot = 1_000_000m, CostSnapshot = 0m }
                    }
                });
            });

            var pickTasksBefore = await QueryAsync(db => db.PickTasks.CountAsync());

            var response = await salesClient.PostAsync($"/api/orders/sales/{orderId}/confirm", null);

            // (a) HTTP — SRS FT-05 NAC-05 yêu cầu 409 cho đơn đã đóng
            response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "SRS FT-05 NAC-05: xác nhận đơn đã huỷ phải trả 409; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) DB không đổi
            var order = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
            order.OrderStatus.Should().Be(OrderStatus.Cancelled);
            order.ConfirmedAt.Should().BeNull();

            // (c) side effect — không sinh phiếu soạn hàng
            (await QueryAsync(db => db.PickTasks.CountAsync()))
                .Should().Be(pickTasksBefore, "đơn đã huỷ không được sinh phiếu soạn hàng");
        }

        // ── L2-FUL-07 — BLOCKED GH-03a ─────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã soạn P1 qty=4; Inventory(P1)=10; JWT nhân viên kho
        // WHEN   POST /api/warehouse/orders/O1/goods-issue
        // THEN   Inventory(P1)=6; có StockTransaction; đơn tiến trạng thái — tất cả trong 1 transaction
        [Fact]
        [Trait("TestID", "L2-FUL-07")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-05 AC-05; BV-02; BR-032; BR-034")]
        [Trait("Blocked", "GH-03a")]
        public async Task L2_FUL_07_PostGoodsIssueDecrementsStockAtomically()
        {
            await ResetAsync();
            var (warehouseClient, _) = await CreateClientAsAsync(SystemRole.WarehouseStaff);

            var orderId = Guid.NewGuid();
            Guid inventoryId = Guid.Empty, productId = Guid.Empty;
            await SeedAsync(async db =>
            {
                // Phải chốt đúng kho WH-DEFAULT (2006d0a6-...) — đơn hàng qua warehouse/orders/{id}/goods-issue
                // trừ tồn ở kho mặc định đó. Từ khi có thêm Inventory ở WH-TRADE/WH-PE cho CÙNG sản phẩm,
                // FirstAsync(i => i.ProductId != null) không còn đảm bảo trúng đúng dòng WH-DEFAULT nữa.
                var defaultLocationId = Guid.Parse("2006d0a6-37a9-46ca-b8a0-bb061ec9f1e9");
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null && i.WarehouseLocationId == defaultLocationId);
                inv.OnHandQuantity = 10; inv.ReservedQuantity = 0; inv.AllocatedQuantity = 4;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0;
                inventoryId = inv.Id; productId = inv.ProductId!.Value;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Orders.Add(new Order
                {
                    Id = orderId, CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 4_000_000m, FinalPayment = 4_000_000m,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Paid,
                    OrderStatus = OrderStatus.Processing,
                    // WarehouseService.cs:691 bắt buộc đã bàn giao mới được post goods-issue (SRS v2 dual handover).
                    FulfillmentStatus = FulfillmentStatus.HandedOver,
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = productId, Quantity = 4, PackedQuantity = 4, PriceSnapshot = 1_000_000m, CostSnapshot = 0m }
                    }
                });
            });

            // ⛔ GH-03a: bảng GoodsIssues thiếu 8 cột mà entity yêu cầu -> "Invalid column name".
            // KHÔNG được vá schema trong test (R3).
            var response = await warehouseClient.PostAsync($"/api/warehouse/orders/{orderId}/goods-issue", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            var inv2 = await ReloadInvAsync(inventoryId);
            inv2.OnHandQuantity.Should().Be(6, "xuất 4 trên tồn 10");
            RawAvailable(inv2).Should().BeGreaterOrEqualTo(0);

            (await QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryId == inventoryId)))
                .Should().BeGreaterThan(0, "xuất kho phải để lại StockTransaction");
        }
    }
}
