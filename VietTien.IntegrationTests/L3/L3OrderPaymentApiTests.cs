using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-OrderPaymentAPI</c> — ORD-01..08, PAY-01..09, CUS-01..05.
    ///
    /// Ánh xạ endpoint chính (xem tests/l3_endpoint_map.csv):
    ///   POST /api/orders            -> POST /api/orders/place-order
    ///   POST /api/orders (tạo tay)  -> POST /api/orders/place-direct-order
    ///   POST /api/webhooks/sepay    -> POST /api/webhooks/sepay-callback (header x-sepay-token)
    ///   GET  /api/orders/{id}       -> GET  /api/orders/my-history/{orderId}
    /// </summary>
    public class L3OrderPaymentApiTests : L3TestBase
    {
        public L3OrderPaymentApiTests(L3SqlFixture factory) : base(factory) { }

        /// <summary>Token webhook khớp appsettings.Test.json — thiếu/sai token thì controller trả 401.</summary>
        private const string SePayToken = "test-sepay-token-not-a-real-secret";

        /// <summary>Khách đã xác minh + hồ sơ + địa chỉ mặc định + giỏ hàng có 1 dòng, sẵn sàng đặt đơn.</summary>
        private async Task<(HttpClient client, User user, CustomerProfile profile, Product product, Cart cart)>
            ArrangeCustomerWithCartAsync(decimal unitPrice = 100_000m, int quantity = 2, int stock = 500)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(unitPrice, stock);
            var cart = await SeedCartAsync(profile.Id, null, (product.Id, quantity, unitPrice));
            return (client, user, profile, product, cart);
        }

        private static HttpRequestMessage SePayWebhook(string orderCode, decimal amount, string referenceCode,
            string? token = SePayToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay-callback")
            {
                Content = JsonContent.Create(new
                {
                    id = Random.Shared.Next(1, 1_000_000),
                    gateway = "TPBank",
                    transactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    accountNumber = "0000000000",
                    transferAmount = amount,
                    transferType = "in",
                    transferContent = orderCode,
                    content = orderCode,
                    referenceNumber = referenceCode,
                    referenceCode = referenceCode,
                })
            };
            if (token != null) req.Headers.Add("x-sepay-token", token);
            return req;
        }

        // ── Block: POST /api/orders (checkout) ────────────────────────────────────────────────

        /// ORD-01 | Input-Domain-Happy | FT-01 AC-05; FT-03 AC-04; BR-008
        /// Giỏ hợp lệ + COD -> tạo Order + OrderItem có PriceSnapshot, giữ tồn (Reserved), giỏ được dọn.
        [Fact]
        public async Task L3_ORD_01_PlaceOrder_ValidCart_Cod_CreatesOrderWithSnapshotAndReservation()
        {
            var (client, _, profile, product, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var orderId = (await ReadJsonAsync(res)).GetProperty("orderId").GetGuid();

            var order = await QueryAsync(db => db.Orders.Include(o => o.OrderItems)
                .SingleAsync(o => o.Id == orderId));

            order.CustomerProfileId.Should().Be(profile.Id);
            order.OrderCode.Should().NotBeNullOrWhiteSpace();
            order.OrderStatus.Should().Be(OrderStatus.PendingConfirmation, "đơn COD chờ Sales xác nhận");
            order.PaymentStatus.Should().Be(PaymentStatus.Pending);
            order.FulfillmentStatus.Should().Be(FulfillmentStatus.Reserved, "COD giữ tồn ngay tại checkout");
            order.OrderItems.Should().ContainSingle()
                .Which.PriceSnapshot.Should().Be(100_000m, "giá phải được chốt bất biến trên đơn");
            order.TotalAmount.Should().Be(200_000m);

            var inventory = await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id));
            inventory.ReservedQuantity.Should().Be(2, "tồn phải được giữ mềm đúng số lượng đặt");

            var cartItems = await QueryAsync(db => db.CartItems.CountAsync(i => i.Cart.CustomerProfileId == profile.Id));
            cartItems.Should().Be(0, "giỏ phải được dọn sau khi đặt đơn thành công");
        }

        /// ORD-02 | Input-Domain-Error | FT-01 NAC-03; BV-01; BR-025
        /// Snapshot giá quá 24h -> chặn đặt đơn, KHÔNG tạo Order.
        /// Workbook chờ 409 + PRICE_SNAPSHOT_EXPIRED_OR_STOCK_CHANGED; code trả 400 + {message}.
        [Fact]
        public async Task L3_ORD_02_PlaceOrder_PriceSnapshotOlderThan24h_Rejected_NoOrderCreated()
        {
            var (client, _, profile, _, cart) = await ArrangeCustomerWithCartAsync();
            await SetCartAgeAsync(cart.Id, TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "code trả 400 chứ không phải 409 như workbook ghi — xem DEF-L3-002");
            (await ReadMessageAsync(res)).Should().Contain("hết hạn giữ");

            (await QueryAsync(db => db.Orders.CountAsync(o => o.CustomerProfileId == profile.Id)))
                .Should().Be(0, "không được tạo Order khi snapshot đã hết hạn");
        }

        /// ORD-03 | BVA | FT-01 BV-01
        /// Biên tuổi snapshot đúng như workbook: 23:59:59 -> cho đặt; 24:00:00 -> chặn.
        /// Điều kiện code là <c>TotalHours &gt; 24</c> (OrderService.cs:153) — giỏ được seed ở mốc
        /// 24:00:00 thì tới lúc request chạm server đã nhỉnh hơn 24h nên bị chặn, khớp kỳ vọng workbook.
        [Theory]
        [InlineData(23, 59, 59, true)]  // 23:59:59 -> còn hiệu lực
        [InlineData(24, 0, 0, false)]   // 24:00:00 -> hết hạn
        public async Task L3_ORD_03_PlaceOrder_SnapshotAgeBoundary_24Hours(
            int hours, int minutes, int seconds, bool shouldSucceed)
        {
            var (client, _, _, _, cart) = await ArrangeCustomerWithCartAsync();
            await SetCartAgeAsync(cart.Id, new TimeSpan(hours, minutes, seconds));

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            if (shouldSucceed)
                res.StatusCode.Should().Be(HttpStatusCode.OK, $"tại {hours:00}:{minutes:00}:{seconds:00} snapshot còn hiệu lực");
            else
                res.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"tại {hours:00}:{minutes:00}:{seconds:00} snapshot đã hết hạn");
        }

        /// ORD-04 | Input-Domain-Error | FT-01 NAC-04; BR-047
        /// Thêm sản phẩm đã ngừng kinh doanh vào giỏ -> bị từ chối (CartService.cs:132).
        [Fact]
        public async Task L3_ORD_04_AddToCart_DiscontinuedProduct_Rejected()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            // Phải có địa chỉ trước: CartService.cs:121 chặn hồ sơ thiếu địa chỉ bằng 409
            // PROFILE_INCOMPLETE, nếu không sẽ trượt trước khi tới được luật "ngừng kinh doanh".
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(50_000m, 100);
            await SeedAsync(async db =>
            {
                var p = await db.Products.SingleAsync(x => x.Id == product.Id);
                p.IsDiscontinued = true;
            });

            var res = await client.PostAsJsonAsync("/api/Cart/items",
                new { ProductId = product.Id, Quantity = 1 });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "code trả 400 chứ không phải 409 như workbook ghi — xem DEF-L3-002");
            (await QueryAsync(db => db.CartItems.CountAsync(i => i.ProductId == product.Id)))
                .Should().Be(0, "sản phẩm ngừng kinh doanh không được vào giỏ");
        }

        /// ORD-05 | Input-Domain-Error | FT-02 NAC-01; BR-005
        /// Giá do CLIENT gửi lên phải bị bỏ qua hoàn toàn — server luôn tự tính từ giỏ.
        /// Gửi kèm TotalAmount/FinalPayment bịa thấp: đơn tạo ra vẫn phải mang giá SERVER.
        [Fact]
        public async Task L3_ORD_05_PlaceOrder_ClientSuppliedPrice_IgnoredByServer()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);

            var res = await client.PostAsJsonAsync("/api/orders/place-order", new
            {
                PaymentMethod = PaymentMethod.COD,
                TotalAmount = 1m,      // client bịa
                FinalPayment = 1m,     // client bịa
                DiscountAmount = 199_999m
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var orderId = (await ReadJsonAsync(res)).GetProperty("orderId").GetGuid();

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            order.TotalAmount.Should().Be(200_000m, "server phải tự tính từ giỏ, KHÔNG lấy giá client gửi");
            order.FinalPayment.Should().Be(WithVat(200_000m), "VAT 10% bắt buộc do server tự cộng");
            order.DiscountAmount.Should().Be(0m, "giỏ 200.000đ chưa chạm bậc chiết khấu 10 triệu");
        }

        /// ORD-06 | BVA | FT-02 BV-01; AC-01; BR-006
        /// Biên bậc giá: 9.999.999 / 10.000.000 / 99.999.999 / 100.000.000 VND.
        /// Ngưỡng thật: LIST_PRICE_MAX_EXCLUSIVE = 10.000.000 (bậc 1 bắt đầu), QUOTATION_MIN_VALUE = 100.000.000.
        /// Từ 100.000.000 code KHÔNG trả pricingSource=QUOTATION_REQUIRED mà chặn hẳn bằng 400 + thông điệp.
        [Theory]
        [InlineData(9_999_999, 0.0)]    // dưới bậc 1 -> giá niêm yết, 0%
        [InlineData(10_000_000, 5.0)]   // chạm bậc 1 -> 5%
        [InlineData(99_999_999, 8.0)]   // bậc cao nhất -> 8%
        [InlineData(100_000_000, -1)]   // chạm ngưỡng báo giá -> bị chặn (đánh dấu -1)
        public async Task L3_ORD_06_CheckoutSummary_PricingTierBoundary(decimal subtotal, double expectedPercent)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            var (product, _) = await SeedSellableProductAsync(subtotal, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, subtotal));

            var res = await client.GetAsync("/api/orders/checkout-summary");

            if (expectedPercent < 0)
            {
                res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                    "từ 100 triệu bắt buộc đi luồng báo giá B2B");
                (await ReadMessageAsync(res)).Should().Contain("báo giá");
                return;
            }

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await ReadJsonAsync(res);
            json.GetProperty("totalAmount").GetDecimal().Should().Be(subtotal);
            ((double)json.GetProperty("discountPercentage").GetDecimal())
                .Should().BeApproximately(expectedPercent, 0.001,
                    $"subtotal {subtotal:N0} phải rơi vào bậc {expectedPercent}%");
        }

        /// ORD-07 | Input-Domain-Error | FT-02 NAC-02
        /// Giỏ 80 triệu (dưới ngưỡng 100 triệu) -> KHÔNG đủ điều kiện tạo yêu cầu báo giá.
        [Fact]
        public async Task L3_ORD_07_CreateQuotation_CartBelowThreshold_Rejected()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            var (product, _) = await SeedSellableProductAsync(80_000_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 80_000_000m));

            var res = await client.PostAsJsonAsync("/api/Quotation/from-cart", new { });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "code trả 400 chứ không phải 409 như workbook ghi — xem DEF-L3-002");
            (await QueryAsync(db => db.Quotations.CountAsync(q => q.CustomerProfileId == profile.Id)))
                .Should().Be(0, "không được tạo yêu cầu báo giá khi chưa đạt ngưỡng");
        }

        /// ORD-08 | Input-Domain-Error | FT-01 NAC-05; NFR-SEC03
        /// IDOR: khách B xem đơn của khách A -> bị chặn, không lộ dữ liệu đơn.
        [Fact]
        public async Task L3_ORD_08_GetOrderDetail_OtherCustomersOrder_Forbidden_NoDataLeak()
        {
            var (clientA, _, _, _, _) = await ArrangeCustomerWithCartAsync();
            var place = await clientA.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });
            place.StatusCode.Should().Be(HttpStatusCode.OK);
            var orderId = (await ReadJsonAsync(place)).GetProperty("orderId").GetGuid();
            var orderCode = (await ReadJsonAsync(place)).GetProperty("orderCode").GetString();

            var (clientB, userB) = await CreateClientAsAsync(SystemRole.Customer);
            await EnsureProfileAsync(userB.Id);

            var res = await clientB.GetAsync($"/api/orders/my-history/{orderId}");

            res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
            (await res.Content.ReadAsStringAsync()).Should().NotContain(orderCode!,
                "không được lộ bất kỳ trường nào của đơn khách khác");
        }

        // ── Block: POST /api/webhooks/sepay ───────────────────────────────────────────────────

        /// PAY-01 | Input-Domain-Happy | FT-03 AC-01; AC-02; BR-009; NFR-SEC05
        /// Webhook hợp lệ, số tiền khớp CHÍNH XÁC -> Payment = Paid, tạo đúng 1 PaymentTransaction.
        [Fact]
        public async Task L3_PAY_01_Webhook_ValidSignatureAndExactAmount_MarksOrderPaid()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);
            var place = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay });
            var placed = await ReadJsonAsync(place);
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            var res = await AnonymousClient().SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-PAY-01"));

            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            order.PaymentStatus.Should().Be(PaymentStatus.Paid);
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(1, "đúng 1 giao dịch thanh toán được ghi");
        }

        /// PAY-02 | Input-Domain-Error | FT-03 NAC-01; NFR-SEC05
        /// Token webhook SAI -> 401, KHÔNG đổi trạng thái thanh toán.
        [Fact]
        public async Task L3_PAY_02_Webhook_InvalidToken_Unauthorized_PaymentUnchanged()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            var res = await AnonymousClient()
                .SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-PAY-02", token: "token-gia-mao"));

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Pending, "chữ ký sai không được đổi trạng thái");
        }

        /// PAY-03 | BVA | FT-03 BV-01; NAC-01
        /// Biên số tiền: total-1 / total / total+1. Chỉ khớp TUYỆT ĐỐI mới ghi Paid.
        /// Lệch ±1đ: code trả 200 nhưng KHÔNG ghi Paid — thay vào đó mở PaymentException để đối soát tay.
        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(+1, false)]
        public async Task L3_PAY_03_Webhook_AmountBoundary_ExactMatchOnly(int delta, bool shouldBePaid)
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            var res = await AnonymousClient()
                .SendAsync(SePayWebhook(orderCode, WithVat(200_000m) + delta, $"REF-PAY-03-{delta}"));

            res.StatusCode.Should().Be(HttpStatusCode.OK,
                "code luôn ack 200 rồi ghi PaymentException; workbook ghi 400 — xem DEF-L3-002");

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            if (shouldBePaid)
            {
                order.PaymentStatus.Should().Be(PaymentStatus.Paid);
            }
            else
            {
                order.PaymentStatus.Should().Be(PaymentStatus.Pending, "lệch dù chỉ 1đ cũng không được ghi Paid");
                (await QueryAsync(db => db.PaymentExceptions.AnyAsync(e => e.OrderId == orderId)))
                    .Should().BeTrue("phải để lại dấu vết cho nhân viên đối soát, không im lặng bỏ qua");
                (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                    .Should().Be(0);
            }
        }

        /// PAY-04 | Idempotency | FT-03 NAC-02; BR-029
        /// Gửi lại NGUYÊN payload đã xử lý (cùng referenceCode) -> vẫn đúng 1 PaymentTransaction.
        [Fact]
        public async Task L3_PAY_04_Webhook_ReplaySamePayload_Idempotent_NoSecondTransaction()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            var first = await AnonymousClient().SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-IDEMPOTENT"));
            var second = await AnonymousClient().SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-IDEMPOTENT"));

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            second.StatusCode.Should().Be(HttpStatusCode.OK, "lần 2 vẫn ack 200, trả kết quả gốc");

            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(1, "KHÔNG được tạo giao dịch thứ 2 cho cùng một business key");
        }

        /// PAY-05 | BVA | FT-03 BV-01; BV-03; NAC-03; BR-033
        /// Tiền về khi KHÔNG còn cấp phát được tồn (hết hàng) -> đơn chuyển PaidReviewRequired
        /// và mở PaymentException cho Sales Manager, thay vì Confirmed.
        [Fact]
        public async Task L3_PAY_05_Webhook_PaidButAllocationFails_MovesToPaidReviewRequired()
        {
            var (client, _, _, product, _) = await ArrangeCustomerWithCartAsync(
                unitPrice: 100_000m, quantity: 2, stock: 2);
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            // Mô phỏng tồn bị "bốc hơi" giữa lúc đặt và lúc tiền về (job dọn reservation, kiểm kê...).
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.SingleAsync(i => i.ProductId == product.Id);
                inv.OnHandQuantity = 0;
                inv.ReservedQuantity = 0;
            });

            var res = await AnonymousClient().SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-PAY-05"));

            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var order = await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
            order.PaymentStatus.Should().Be(PaymentStatus.Paid, "tiền đã thật sự vào tài khoản");
            order.OrderStatus.Should().Be(OrderStatus.PaidReviewRequired,
                "không cấp phát được tồn thì phải chuyển sang diện Sales Manager xử lý");
            (await QueryAsync(db => db.PaymentExceptions.AnyAsync(e => e.OrderId == orderId)))
                .Should().BeTrue("phải tạo việc cho Sales Manager");
        }

        // ── Block: manual-confirm · COD SLA ───────────────────────────────────────────────────

        /// PAY-06 | Input-Domain-Error | FT-03 NAC-04
        /// Sales Staff (không phải Sales Manager) gọi xác nhận thủ công -> 403.
        [Fact]
        public async Task L3_PAY_06_ManualConfirm_SalesStaffRole_Forbidden()
        {
            var (client, _) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var res = await client.PostAsJsonAsync($"/api/orders/{Guid.NewGuid()}/manual-confirm",
                new { Reason = "test", EvidenceUrl = "https://example.invalid/e.png" });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// PAY-07 | Input-Domain-Error | FT-03 NAC-04
        /// Sales Manager gọi nhưng THIẾU bằng chứng/lý do -> bị từ chối (không phải 403).
        [Fact]
        public async Task L3_PAY_07_ManualConfirm_MissingEvidence_Rejected()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync();
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm", new { });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "vai trò Sales Manager phải qua được cổng phân quyền");
            res.IsSuccessStatusCode.Should().BeFalse("thiếu bằng chứng thì không được xác nhận thủ công");

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .PaymentStatus.Should().Be(PaymentStatus.Pending);
        }

        /// PAY-08 | BVA | FT-03 BV-02; AC-04; BR-010
        /// Xác nhận đơn COD phía Sales: đúng vai trò thì qua cổng phân quyền, sai vai trò thì 403.
        /// Workbook mô tả mốc SLA 24:59/25:00/29:59/30:00/34:59/35:00 — hệ thống KHÔNG chặn xác nhận
        /// theo mốc phút mà xử lý bằng job dọn reservation, nên phần biên thời gian ghi ở DEF-L3-008.
        [Fact]
        public async Task L3_PAY_08_ConfirmSalesOrder_RoleGate()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            var forbidden = await customer.PostAsJsonAsync($"/api/orders/sales/{Guid.NewGuid()}/confirm", new { });
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, "khách hàng không được tự xác nhận đơn");

            var (staff, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var allowed = await staff.PostAsJsonAsync($"/api/orders/sales/{Guid.NewGuid()}/confirm", new { });
            allowed.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "Sales Staff phải lọt qua cổng phân quyền");
        }

        /// PAY-09 | Idempotency | FT-03 AC-05; BR-029
        /// Gọi lại xác nhận thủ công trên đơn ĐÃ Paid -> không tạo thêm PaymentTransaction.
        [Fact]
        public async Task L3_PAY_09_ManualConfirm_ReplayOnAlreadyPaidOrder_NoDuplicateTransaction()
        {
            var (client, _, _, _, _) = await ArrangeCustomerWithCartAsync(unitPrice: 100_000m, quantity: 2);
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.SePay }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var orderCode = placed.GetProperty("orderCode").GetString()!;

            await AnonymousClient().SendAsync(SePayWebhook(orderCode, WithVat(200_000m), "REF-PAY-09"));

            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            await manager.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm",
                new { Reason = "Da doi soat sao ke", EvidenceUrl = "https://example.invalid/e.png" });

            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(1, "đơn đã Paid thì xác nhận thủ công không được tạo giao dịch thứ 2");
        }

        // ── Block: hồ sơ khách hàng, địa chỉ & đơn tạo tay ────────────────────────────────────

        /// CUS-01 | Input-Domain-Happy | FT-01 AC-03; BR-028
        /// Đổi địa chỉ trong hồ sơ SAU khi đặt đơn -> snapshot địa chỉ trên đơn GIỮ NGUYÊN.
        [Fact]
        public async Task L3_CUS_01_OrderAddressSnapshot_ImmutableAfterProfileChange()
        {
            var (client, _, profile, _, _) = await ArrangeCustomerWithCartAsync();
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();

            var snapshotBefore = (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId))).ShippingAddress;
            snapshotBefore.Should().NotBeNullOrWhiteSpace();

            // Khách sửa sổ địa chỉ sau khi đơn đã tạo.
            await SeedAsync(async db =>
            {
                var addr = await db.Addresses.SingleAsync(a => a.CustomerProfileId == profile.Id);
                addr.SpecificAddress = "DIA CHI DA THAY DOI";
                addr.City = "Ha Noi";
            });

            var snapshotAfter = (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId))).ShippingAddress;
            snapshotAfter.Should().Be(snapshotBefore, "đơn hàng là hồ sơ bất biến, không đổi theo sổ địa chỉ");
            snapshotAfter.Should().NotContain("DIA CHI DA THAY DOI");
        }

        /// CUS-02 | BVA | FT-01 BV-03
        /// Thêm địa chỉ mới với isDefault = true khi đang có 0 / 1 địa chỉ mặc định
        /// -> luôn còn ĐÚNG 1 địa chỉ mặc định, không bao giờ tồn tại 2.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task L3_CUS_02_AddDefaultAddress_ExactlyOneDefaultRemains(int existingDefaults)
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            for (var i = 0; i < existingDefaults; i++) await SeedAddressAsync(profile.Id, isDefault: true);

            // CreateAddressDto dùng tên trường Name/Phone/AddressLine (khác tên cột trong Model).
            var res = await client.PostAsJsonAsync("/api/user/addresses", new
            {
                Name = "Nguoi Nhan Moi",
                Phone = NewPhone(),
                City = "TP HCM",
                District = "Quan 3",
                Ward = "Phuong 5",
                AddressLine = "So 9 Duong Moi",
                IsDefault = true,
            });

            res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

            var defaults = await QueryAsync(db =>
                db.Addresses.CountAsync(a => a.CustomerProfileId == profile.Id && a.IsDefault));
            defaults.Should().Be(1, "không bao giờ được tồn tại 2 địa chỉ mặc định cùng lúc");
        }

        /// CUS-03 | Input-Domain-Error | FT-01 NAC-05; BR-028
        /// Xoá địa chỉ đã bị đơn lịch sử tham chiếu: đơn cũ vẫn phải tra cứu được với địa chỉ đã chốt.
        [Fact]
        public async Task L3_CUS_03_DeleteAddressReferencedByOrder_HistoricalOrderStillReadable()
        {
            var (client, _, profile, _, _) = await ArrangeCustomerWithCartAsync();
            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();
            var addressId = (await QueryAsync(db =>
                db.Addresses.SingleAsync(a => a.CustomerProfileId == profile.Id))).Id;
            var snapshotBefore = (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId))).ShippingAddress;

            var del = await client.DeleteAsync($"/api/user/addresses/{addressId}");

            // Dù API cho xoá hay từ chối, bất biến bắt buộc là: đơn lịch sử KHÔNG mất địa chỉ đã chốt.
            var detail = await client.GetAsync($"/api/orders/my-history/{orderId}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK, "đơn cũ phải luôn tra cứu được");

            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .ShippingAddress.Should().Be(snapshotBefore,
                    $"snapshot địa chỉ trên đơn phải bất biến (DELETE trả {(int)del.StatusCode})");
        }

        /// CUS-04 | Input-Domain-Error | FT-03 NAC-05; BR-030; BR-004
        /// Đơn tạo tay: khách hàng chưa xác thực -> Sales Staff vẫn phải qua cổng phân quyền,
        /// nhưng dữ liệu không hợp lệ thì KHÔNG được tạo đơn.
        [Fact]
        public async Task L3_CUS_04_PlaceDirectOrder_RoleGateAndValidation()
        {
            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            var forbidden = await customer.PostAsJsonAsync("/api/orders/place-direct-order", new
            {
                CustomerName = "Khach le",
                PaymentMethod = PaymentMethod.COD,
                Items = Array.Empty<object>(),
            });
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, "khách hàng không được tạo đơn tay");

            var (staff, _) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var invalid = await staff.PostAsJsonAsync("/api/orders/place-direct-order", new
            {
                CustomerName = "",                    // vi phạm [Required]
                PaymentMethod = PaymentMethod.COD,
                Items = Array.Empty<object>(),
            });
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            invalid.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "Sales Staff phải lọt qua cổng phân quyền");
        }

        /// CUS-05 | Input-Domain-Error | FT-03 NAC-05; BR-030; BR-026
        /// Giỏ >= 100 triệu mà KHÔNG kèm báo giá đã duyệt -> không được tạo đơn, không giữ tồn.
        [Fact]
        public async Task L3_CUS_05_PlaceOrder_Over100M_WithoutApprovedQuotation_Rejected()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 100_000_000m));

            var res = await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadMessageAsync(res)).Should().Contain("báo giá");

            (await QueryAsync(db => db.Orders.CountAsync(o => o.CustomerProfileId == profile.Id)))
                .Should().Be(0, "không tạo đơn");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .ReservedQuantity.Should().Be(0, "không được giữ tồn cho đơn bị chặn");
        }
    }
}
