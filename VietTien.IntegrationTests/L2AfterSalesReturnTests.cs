using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-AfterSalesReturn — đổi/trả, thu hồi, huỷ đơn đã thanh toán.
    ///
    /// RET-01…04 phụ thuộc entity ReturnExchangeRequest — bảng `ReturnExchangeRequests` (+2 bảng con)
    /// nay ĐÃ có trong migration (GH-03b đã fix, xem FIX_PLAN_2026-08-07). Trước đó SeedStockAsync gán
    /// đơn cho CustomerProfile bất kỳ trong DB thay vì đúng khách hàng gọi API trong test — sau khi
    /// L1-ORD-51 (chặn IDOR tạo yêu cầu đổi/trả trên đơn của khách khác) được fix đúng, lỗi setup này lộ
    /// ra thành 400 "không có quyền". Đã sửa: SeedStockAsync nay tạo CustomerProfile đúng UserId khách.
    /// RET-05…07 đi qua luồng huỷ-đơn-đã-trả của DeliveryController, không đụng các bảng đó.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2AfterSalesReturnTests : SqlServerTestBase
    {
        public L2AfterSalesReturnTests(SqlServerFixture factory) : base(factory) { }

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        private sealed record Ctx(Guid ProfileId, Guid ProductId, Guid InventoryId);

        /// <param name="customerUserId">
        /// Nếu truyền vào: tạo 1 CustomerProfile MỚI gắn đúng UserId này, để đơn seed thuộc về đúng
        /// khách hàng đang gọi API trong test (bắt buộc với các case gọi exchange-request bằng JWT
        /// customer — nếu không, OrderService chặn IDOR đúng theo L1-ORD-51 vì đơn thuộc khách khác).
        /// Nếu để null: giữ hành vi cũ (lấy CustomerProfile bất kỳ có sẵn) — dùng cho case không cần
        /// khớp danh tính khách hàng (RET-05/06/07 gọi bằng JWT Manager/Sales, không phải Customer).
        /// </param>
        private async Task<Ctx> SeedStockAsync(int onHand, int allocated = 0, Guid? customerUserId = null)
        {
            Guid profileId = Guid.Empty, productId = Guid.Empty, inventoryId = Guid.Empty;
            await SeedAsync(async db =>
            {
                // Phải chốt đúng kho tập kết WH-DEFAULT (2006d0a6-...) — ConfirmPickupAsync cộng hàng
                // thu hồi vào Quarantine của kho này; nếu chọn nhầm dòng Inventory ở WH-TRADE/WH-PE thì
                // assertion trên c.InventoryId sẽ không bao giờ thấy QuarantineQuantity tăng.
                var defaultLocationId = Guid.Parse("2006d0a6-37a9-46ca-b8a0-bb061ec9f1e9");
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null && i.WarehouseLocationId == defaultLocationId);
                inv.OnHandQuantity = onHand; inv.ReservedQuantity = 0; inv.AllocatedQuantity = allocated;
                inv.DamagedQuantity = 0; inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                productId = inv.ProductId!.Value; inventoryId = inv.Id;
                foreach (var o in await db.Inventories.Where(i => i.ProductId == productId && i.Id != inv.Id).ToListAsync())
                { o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0; }

                if (customerUserId is { } userId)
                {
                    profileId = Guid.NewGuid();
                    db.CustomerProfiles.Add(new CustomerProfile { Id = profileId, UserId = userId });
                }
                else
                {
                    profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                }
            });
            return new Ctx(profileId, productId, inventoryId);
        }

        private async Task<Guid> SeedOrderAsync(Ctx c, int qty, decimal total,
            OrderStatus status, PaymentStatus payment, DeliveryStatus delivery = DeliveryStatus.NotScheduled,
            bool withPaymentTransaction = false)
        {
            var id = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                db.Orders.Add(new Order
                {
                    Id = id, CustomerProfileId = c.ProfileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = total, FinalPayment = total,
                    PaymentMethod = PaymentMethod.SePay, PaymentStatus = payment,
                    OrderStatus = status, DeliveryStatus = delivery,
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = c.ProductId, Quantity = qty, PriceSnapshot = total / qty, CostSnapshot = 0m }
                    }
                });
                if (withPaymentTransaction)
                {
                    db.PaymentTransactions.Add(new PaymentTransaction
                    {
                        OrderId = id, TransactionId = "FT" + Guid.NewGuid().ToString("N")[..10],
                        Amount = total, AccountNumber = "x", ReferenceCode = "ret",
                        IsSuccess = true, Timestamp = DateTime.UtcNow
                    });
                }
                await Task.CompletedTask;
            });
            return id;
        }

        private Task<Order> ReloadOrderAsync(Guid id) =>
            QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == id));

        // ── L2-RET-01 — BLOCKED GH-03b ─────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã giao, dòng P1 giao 5; tồn khả dụng P1 = 10
        // WHEN   Tạo yêu cầu đổi/trả → Manager duyệt → lên lịch thu hồi → xác nhận đã thu hồi
        // THEN   Sau duyệt: Approved + có nhiệm vụ thu hồi; sau thu hồi: hàng vào cách ly, RawAvailable VẪN = 10
        [Fact]
        [Trait("TestID", "L2-RET-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 AC-05; BR-019")]
        [Trait("Blocked", "GH-03b")]
        public async Task L2_RET_01_ReturnRequestThroughPickupKeepsAvailabilityUnchanged()
        {
            await ResetAsync();
            var (customer, customerUser) = await CreateClientAsAsync(SystemRole.Customer);
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var c = await SeedStockAsync(onHand: 10, customerUserId: customerUser.Id);
            var orderId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.Completed, PaymentStatus.Paid, DeliveryStatus.Delivered);

            var availableBefore = RawAvailable(
                await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId)));

            // ⛔ GH-03b: bảng ReturnExchangeRequests chưa từng được migration tạo ra.
            var create = await customer.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request",
                new { ReturnItems = new[] { new { ProductId = c.ProductId, Quantity = 2 } }, Reason = "loi san pham" });
            create.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await create.Content.ReadAsStringAsync());

            var requestId = await QueryAsync(db => db.ReturnExchangeRequests.AsNoTracking()
                .Where(r => r.OrderId == orderId).Select(r => r.Id).FirstAsync());

            var approve = await manager.PostAsJsonAsync($"/api/orders/exchange-request/{requestId}/process",
                new { IsApproved = true });
            approve.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await approve.Content.ReadAsStringAsync());

            (await manager.PostAsJsonAsync($"/api/delivery/pickups/{requestId}/schedule",
                new { VehicleId = 1, Shift = "Sáng", PickupDate = DateTime.UtcNow.AddDays(1) })).EnsureSuccessStatusCode();

            // Bước "Tiếp nhận xe hoàn" ở kho: ConfirmPickupAsync nay CHỈ cộng OnHand và trông chờ
            // QuarantineQuantity đã được cộng trước đó bởi POST /api/warehouse-management/quarantine/receive
            // (OrderService.cs:3464). Bỏ bước này thì hàng thu hồi rơi thẳng vào tồn khả dụng.
            var (warehouseClient, warehouseStaff) = await CreateClientAsAsync(SystemRole.WarehouseStaff);
            await SeedAsync(async db =>
            {
                var defaultWarehouseId = await db.Warehouses.Where(w => w.Code == "WH-DEFAULT")
                    .Select(w => w.Id).FirstAsync();
                var staffUser = await db.Users.FirstAsync(u => u.Id == warehouseStaff.Id);
                staffUser.AssignedWarehouseId = defaultWarehouseId;
            });

            var quarantine = await warehouseClient.PostAsJsonAsync("/api/warehouse-management/quarantine/receive",
                new { OrderId = orderId, ProductId = c.ProductId, Quantity = 2, Reason = "Hang loi khach tra" });
            quarantine.StatusCode.Should().Be(HttpStatusCode.OK,
                "kho phải nhập được hàng thu hồi vào khu cách ly; body: {0}",
                await quarantine.Content.ReadAsStringAsync());

            (await manager.PostAsync($"/api/delivery/pickups/{requestId}/confirm", null)).EnsureSuccessStatusCode();

            // (b) hàng thu hồi vào khu cách ly -> khả dụng KHÔNG tăng
            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == c.InventoryId));
            RawAvailable(inv).Should().Be(availableBefore,
                "BR-019: hàng thu hồi phải vào cách ly, không được cộng thẳng vào khả dụng");
            inv.QuarantineQuantity.Should().BeGreaterThan(0);
        }

        // ── L2-RET-02 — BLOCKED GH-03b ─────────────────────────────────────────────────────

        // GIVEN  Yêu cầu R1 đang chờ duyệt; người gọi mang JWT Sales Staff
        // WHEN   POST duyệt yêu cầu R1 bằng JWT Sales Staff
        // THEN   403; trạng thái R1 không đổi; không tạo nhiệm vụ thu hồi
        [Fact]
        [Trait("TestID", "L2-RET-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 NAC-05; NFR-SEC03")]
        [Trait("Blocked", "GH-03b")]
        public async Task L2_RET_02_SalesStaffCannotApproveReturnRequest()
        {
            await ResetAsync();
            var (customer, customerUser) = await CreateClientAsAsync(SystemRole.Customer);
            var (salesStaff, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var c = await SeedStockAsync(onHand: 10, customerUserId: customerUser.Id);
            var orderId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.Completed, PaymentStatus.Paid, DeliveryStatus.Delivered);

            // ⛔ GH-03b
            (await customer.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request",
                new { ReturnItems = new[] { new { ProductId = c.ProductId, Quantity = 2 } }, Reason = "loi" }))
                .EnsureSuccessStatusCode();

            var request = await QueryAsync(db => db.ReturnExchangeRequests.AsNoTracking()
                .FirstAsync(r => r.OrderId == orderId));

            var response = await salesStaff.PostAsJsonAsync(
                $"/api/orders/exchange-request/{request.Id}/process", new { IsApproved = true });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await QueryAsync(db => db.ReturnExchangeRequests.AsNoTracking().FirstAsync(r => r.Id == request.Id)))
                .Status.Should().Be(request.Status, "trạng thái không được đổi");
        }

        // ── L2-RET-03 — BLOCKED GH-03b ─────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã giao P1 = 5
        // WHEN   Tạo yêu cầu đổi/trả với qty = 0 / 5 / 6
        // THEN   0 và 6 bị từ chối; 5 được chấp nhận; case bị từ chối không tạo bản ghi nào
        [Theory]
        [Trait("TestID", "L2-RET-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 BV-03")]
        [Trait("Blocked", "GH-03b")]
        [InlineData(0, false)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        public async Task L2_RET_03_ReturnQuantityMustBeWithinDeliveredQuantity(int qty, bool expectAccepted)
        {
            await ResetAsync();
            var (customer, customerUser) = await CreateClientAsAsync(SystemRole.Customer);
            var c = await SeedStockAsync(onHand: 10, customerUserId: customerUser.Id);
            var orderId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.Completed, PaymentStatus.Paid, DeliveryStatus.Delivered);

            // ⛔ GH-03b
            var response = await customer.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request",
                new { ReturnItems = new[] { new { ProductId = c.ProductId, Quantity = qty } }, Reason = "kiem bien" });

            var count = await QueryAsync(db => db.ReturnExchangeRequests.CountAsync(r => r.OrderId == orderId));

            if (expectAccepted)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await response.Content.ReadAsStringAsync());
                count.Should().Be(1);
            }
            else
            {
                ((int)response.StatusCode).Should().BeInRange(400, 499,
                    "BV-03: số lượng {0} nằm ngoài khoảng hợp lệ (1..5)", qty);
                count.Should().Be(0, "yêu cầu bị từ chối không được để lại bản ghi");
            }
        }

        // ── L2-RET-04 — BLOCKED GH-03b ─────────────────────────────────────────────────────

        // GIVEN  R1 đã duyệt; P1 giá gốc 50.000; P2 giá hiện hành 70.000
        // WHEN   Tạo đơn thay thế cùng mã P1, rồi tạo đơn thay thế đổi sang P2
        // THEN   Cùng mã dùng giá gốc, không phát sinh phải thu; khác mã dùng giá hiện hành, ghi rõ chênh lệch
        [Fact]
        [Trait("TestID", "L2-RET-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 AC-03; BR-042; BR-043")]
        [Trait("Blocked", "GH-03b")]
        public async Task L2_RET_04_ReplacementPricingDependsOnSameOrDifferentSku()
        {
            await ResetAsync();
            var (customer, customerUser) = await CreateClientAsAsync(SystemRole.Customer);
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var c = await SeedStockAsync(onHand: 100, customerUserId: customerUser.Id);
            var orderId = await SeedOrderAsync(c, 5, 250_000m,
                OrderStatus.Completed, PaymentStatus.Paid, DeliveryStatus.Delivered);

            // ⛔ GH-03b
            (await customer.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request",
                new { ReturnItems = new[] { new { ProductId = c.ProductId, Quantity = 1 } }, Reason = "doi hang" }))
                .EnsureSuccessStatusCode();

            var requestId = await QueryAsync(db => db.ReturnExchangeRequests.AsNoTracking()
                .Where(r => r.OrderId == orderId).Select(r => r.Id).FirstAsync());
            (await manager.PostAsJsonAsync($"/api/orders/exchange-request/{requestId}/process",
                new { IsApproved = true })).EnsureSuccessStatusCode();

            var sameSku = await manager.PostAsJsonAsync($"/api/delivery/exchange/{requestId}/replacement",
                new { Items = new[] { new { ProductId = c.ProductId, Quantity = 1, Price = 50_000m } } });
            sameSku.StatusCode.Should().Be(HttpStatusCode.OK,
                "BR-042: đổi cùng mã phải dùng giá gốc; body: {0}", await sameSku.Content.ReadAsStringAsync());
        }

        // ── L2-RET-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã PAID qua SePay, chưa giao
        // WHEN   Yêu cầu huỷ → Manager duyệt và tạo đơn thay thế
        // THEN   PaymentStatus VẪN Paid; KHÔNG tạo giao dịch hoàn tiền; đơn gốc đóng, đơn thay thế tham chiếu đơn gốc
        [Fact]
        [Trait("TestID", "L2-RET-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 AC-01; BR-017")]
        public async Task L2_RET_05_CancelPaidOrderKeepsPaidStatusAndCreatesLinkedReplacement()
        {
            await ResetAsync();
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var c = await SeedStockAsync(onHand: 100, allocated: 5);
            var originalId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.CancelRequested, PaymentStatus.Paid,
                withPaymentTransaction: true);

            var txBefore = await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == originalId));

            var response = await manager.PostAsJsonAsync(
                $"/api/delivery/{originalId}/approve-cancel-replacement",
                new CreateReplacementOrderDto
                {
                    OriginalOrderId = originalId,
                    Items = new List<ReplacementOrderItemDto>
                    {
                        new() { ProductId = c.ProductId, Quantity = 3, Price = 1_000_000m }
                    }
                });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            var original = await ReloadOrderAsync(originalId);
            original.PaymentStatus.Should().Be(PaymentStatus.Paid,
                "BR-017: huỷ đơn đã trả tiền KHÔNG được đổi PaymentStatus khỏi Paid");
            original.OrderStatus.Should().BeOneOf(
                new[] { OrderStatus.Cancelled, OrderStatus.CancelledReallocated },
                "đơn gốc phải đóng lại");
            original.ReplacementOrderId.Should().NotBeNull("đơn gốc phải trỏ tới đơn thay thế");

            var transactions = await QueryAsync(db => db.PaymentTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).ToListAsync());
            transactions.Count.Should().Be(txBefore, "BR-017: không được tạo giao dịch hoàn tiền");
            transactions.Should().OnlyContain(t => t.Amount >= 0, "không được có giao dịch âm");

            // (c) side effect — giá trị đã trả được chuyển sang đơn thay thế
            var reallocated = (await QueryAsync(db => db.PaymentReallocations.AsNoTracking()
                .Where(r => r.OriginalOrderId == originalId).SumAsync(r => (decimal?)r.Amount))) ?? 0m;
            var credited = (await QueryAsync(db => db.CreditTransactions.AsNoTracking()
                .Where(t => t.OrderId == originalId).SumAsync(t => (decimal?)t.Amount))) ?? 0m;
            (reallocated + credited).Should().Be(5_000_000m,
                "giá trị đã trả phải được bảo toàn trọn vẹn sang đơn thay thế và/hoặc phần dư");
        }

        // ── L2-RET-06 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đang trên đường giao (In Delivery)
        // WHEN   POST yêu cầu huỷ
        // THEN   409; không tạo yêu cầu huỷ; trạng thái đơn không đổi
        [Fact]
        [Trait("TestID", "L2-RET-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-08 NAC-05; 4.4.1")]
        public async Task L2_RET_06_CannotRequestCancelWhileOrderIsInDelivery()
        {
            await ResetAsync();
            var (sales, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var c = await SeedStockAsync(onHand: 100, allocated: 5);
            var orderId = await SeedOrderAsync(c, 5, 5_000_000m,
                OrderStatus.Processing, PaymentStatus.Paid, DeliveryStatus.InDelivery,
                withPaymentTransaction: true);

            var before = await ReloadOrderAsync(orderId);

            var response = await sales.PostAsJsonAsync($"/api/delivery/{orderId}/request-cancel",
                new CancelPaidOrderRequestDto { Reason = "khach doi y" });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "SRS 4.4.1: đơn đang trên đường giao không được nhận yêu cầu huỷ; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) DB — không đổi gì
            var after = await ReloadOrderAsync(orderId);
            after.OrderStatus.Should().Be(before.OrderStatus);
            after.DeliveryStatus.Should().Be(DeliveryStatus.InDelivery);
            after.CancelRequestedAt.Should().BeNull("không được ghi nhận yêu cầu huỷ");
            after.CancelReason.Should().BeNull();
        }

        // ── L2-RET-07 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn O1 đã PAID, đang chờ duyệt huỷ
        // WHEN   Bắn SONG SONG 2 request duyệt huỷ (cùng barrier)
        // THEN   Đúng 1 request thành công; chỉ 1 đơn thay thế; giá trị đã trả không bị nhân đôi
        [Fact]
        [Trait("TestID", "L2-RET-07")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "NFR-A05; BR-029")]
        public async Task L2_RET_07_ConcurrentCancelApprovalsCreateExactlyOneReplacement()
        {
            await ResetAsync();
            var (manager, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            for (var iteration = 1; iteration <= 3; iteration++)
            {
                var c = await SeedStockAsync(onHand: 1000, allocated: 5);
                var originalId = await SeedOrderAsync(c, 5, 5_000_000m,
                    OrderStatus.CancelRequested, PaymentStatus.Paid, withPaymentTransaction: true);

                CreateReplacementOrderDto Body() => new()
                {
                    OriginalOrderId = originalId,
                    Items = new List<ReplacementOrderItemDto>
                    {
                        new() { ProductId = c.ProductId, Quantity = 3, Price = 1_000_000m }
                    }
                };

                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var t1 = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await manager.PostAsJsonAsync($"/api/delivery/{originalId}/approve-cancel-replacement", Body());
                });
                var t2 = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await manager.PostAsJsonAsync($"/api/delivery/{originalId}/approve-cancel-replacement", Body());
                });

                barrier.SetResult();
                var responses = await Task.WhenAll(t1, t2);
                var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));

                // (a) đúng 1 bên thắng
                responses.Count(r => r.IsSuccessStatusCode).Should().Be(1,
                    "vòng {0}: duyệt huỷ song song chỉ được thành công 1 lần. Phản hồi: {1}",
                    iteration, string.Join(" || ", responses.Select((r, i) => $"{(int)r.StatusCode}:{bodies[i]}")));

                // (b) DB — chỉ 1 đơn thay thế, giá trị không nhân đôi
                var replacements = await QueryAsync(db => db.Orders.AsNoTracking()
                    .CountAsync(o => o.ReplacementOrderId != null && o.Id == originalId));
                replacements.Should().BeLessThanOrEqualTo(1, "vòng {0}", iteration);

                var reallocated = (await QueryAsync(db => db.PaymentReallocations.AsNoTracking()
                    .Where(r => r.OriginalOrderId == originalId).SumAsync(r => (decimal?)r.Amount))) ?? 0m;
                reallocated.Should().BeLessThanOrEqualTo(5_000_000m,
                    "vòng {0}: BR-029 — giá trị đã trả không được phân bổ nhân đôi", iteration);

                (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == originalId)))
                    .Should().Be(1, "vòng {0}: không được sinh thêm giao dịch thanh toán", iteration);
            }
        }
    }
}
