using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;
using VietTien.API.Models;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — L1-ORD-01..47. EF InMemory + UnitOfWork/CartService thật +
    /// mock IEmailService/INotificationService/ICloudinaryService + IConfiguration in-memory.
    ///
    /// Khác biệt lớn giữa spec và code thật (đã ghi chú từng case):
    /// - Bậc chiết khấu thực tế: &lt;10M: 0% | 10M+: 5% | 31M+: 6% | 51M+: 7% | 71M+: 8% | &gt;=100M: THROW (yêu cầu báo giá B2B).
    /// - Không có cơ chế auto-áp giá thỏa thuận ở checkout (L1-ORD-04 Blocked).
    /// - Webhook SePay đối soát bằng "&gt;=" thay vì khớp chính xác từng đồng (L1-ORD-16 lệch spec).
    /// - 'Order Received' = OrderStatus.PendingConfirmation; 'In Delivery' = DeliveryStatus.InDelivery.
    /// </summary>
    /// Các block bổ sung của doc v2.2 nằm ở các file partial cùng tên:
    /// OrderServiceTests.ReturnExchange.cs (ORD-48..60) · OrderServiceTests.Scoping.cs (ORD-61..70)
    /// · OrderServiceTests.DeliveryFailure.cs (ORD-72..73).
    public partial class OrderServiceTests
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<IEmailService> _email = new();
        private readonly Mock<INotificationService> _noti = new();
        // Mock cấu hình hệ thống — giữ lại field để các file partial (vd L1-ORD-73) đổi được ngưỡng runtime.
        private readonly Mock<ISystemConfigService> _sysConfig = new();
        private readonly OrderService _sut;
        private readonly User _customer;
        private readonly CustomerProfile _profile;
        private readonly User _salesStaff;

        public OrderServiceTests()
        {
            _db = TestDbFactory.Create();
            var uow = new UnitOfWork(_db);
            var cartService = new CartService(uow, _db);

            // Ngưỡng báo giá B2B + bậc chiết khấu giờ đọc từ Admin config/DiscountTier (DB) —
            // mock theo đúng dữ liệu seed production: 100M threshold; 10M+:5%, 31M+:6%, 51M+:7%, 71M+:8%.
            _sysConfig.Setup(s => s.GetEffectiveValueAsync("QUOTATION_MIN_VALUE", It.IsAny<DateTime?>()))
                      .ReturnsAsync("100000000");
            // Ngưỡng khoá giao hàng theo seed production (ApplicationDbContext) — L1-ORD-73 ghi đè giá trị này.
            _sysConfig.Setup(s => s.GetEffectiveValueAsync("DELIVERY_FAILURE_MANAGER_THRESHOLD", It.IsAny<DateTime?>()))
                      .ReturnsAsync("3");
            var discountTiers = new Mock<IDiscountTierService>();
            discountTiers.Setup(d => d.GetApplicableDiscountPercentAsync(It.IsAny<decimal>()))
                         .ReturnsAsync((decimal amount) =>
                             amount >= 71_000_000m ? 0.08m :
                             amount >= 51_000_000m ? 0.07m :
                             amount >= 31_000_000m ? 0.06m :
                             amount >= 10_000_000m ? 0.05m : 0m);

            _sut = new OrderService(uow, _db, TestConfig.Create(), cartService,
                _email.Object, _noti.Object, new Mock<ICloudinaryService>().Object,
                _sysConfig.Object, discountTiers.Object, new FakeInventoryReservationService(_db));

            _salesStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(_salesStaff);
            (_customer, _profile) = TestData.SeedCustomer(_db);
            _profile.AssignedSalesStaffId = _salesStaff.Id;
            _db.SaveChanges();
        }

        /// <summary>Seed giỏ hàng có 1 sản phẩm với tổng tiền cho trước, kèm tồn kho đủ lớn (mặc định)
        /// để phản ánh đúng việc PlaceOrderAsync giữ mềm tồn kho (ReserveAsync) tại checkout.</summary>
        private Cart SeedCartWithTotal(decimal total, int qty = 1, int stock = 1_000_000)
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = total / qty);
            TestData.SeedInventory(_db, product.Id, stock);
            var cart = TestData.Cart(_profile.Id);
            _db.Carts.Add(cart);
            _db.CartItems.Add(TestData.CartItem(cart.Id, product.Id, qty, total / qty));
            _db.SaveChanges();
            return cart;
        }

        private Order SeedOrder(Action<Order>? mutate = null)
        {
            var order = TestData.Order(_profile.Id, mutate);
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        private SePayWebhookDto WebhookPayload(string orderCode, decimal amount) => new()
        {
            content = $"CK {orderCode}",
            transferAmount = amount,
            accountNumber = "0123456789",
            referenceCode = $"REF-{Guid.NewGuid():N}"[..12]
        };

        //  ▶ Block: GetCheckoutSummaryAsync() — tiered pricing

        // L1-ORD-01 | BVA-Max | Tổng 9.999.999 -> bậc chuẩn, KHÔNG chiết khấu
        [Fact]
        public async Task L1_ORD_01_Checkout_9999999_StandardNoDiscount()
        {
            SeedCartWithTotal(9_999_999m);

            var summary = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            summary.TotalAmount.Should().Be(9_999_999m);
            summary.DiscountPercentage.Should().Be(0);
            summary.DiscountAmount.Should().Be(0);
        }

        // L1-ORD-02 | BVA-Min | Tổng đúng 10.000.000 -> nhảy bậc chiết khấu 5%
        [Fact]
        public async Task L1_ORD_02_Checkout_10M_Tier5PercentApplied()
        {
            SeedCartWithTotal(10_000_000m);

            var summary = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            summary.DiscountPercentage.Should().Be(5);
            summary.DiscountAmount.Should().Be(500_000m);
            summary.FinalPayment.Should().Be(9_500_000m); // không VAT (chưa có MST)
        }

        // L1-ORD-03 | BVA-Max | Tổng 99.999.999 -> vẫn là bậc chiết khấu (8%), CHƯA rơi vào luồng báo giá B2B
        [Fact]
        public async Task L1_ORD_03_Checkout_99999999_StillDiscountTier()
        {
            SeedCartWithTotal(99_999_999m);

            var summary = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            summary.DiscountPercentage.Should().Be(8); // bậc cao nhất dưới 100M
        }

        // L1-ORD-04 | SKIP/Blocked — auto-áp giá thỏa thuận (SavedB2BPriceSnapshot) cho đơn >= 100M
        // chưa được cài đặt: GetCheckoutSummaryAsync luôn throw khi >= 100M bất kể có giá thỏa thuận.
        [Fact(Skip = "Blocked: auto-áp giá thỏa thuận cho đơn >= 100M chưa cài đặt — mọi đơn >= 100M đều bị chặn yêu cầu báo giá B2B.")]
        public void L1_ORD_04_Checkout_100M_WithNegotiatedPrice_AutoApplied() { }

        // L1-ORD-05 | Guard-FALSE | Tổng >= 100M -> chặn checkout, yêu cầu báo giá B2B
        [Fact]
        public async Task L1_ORD_05_Checkout_Above100M_RequiresQuotation()
        {
            SeedCartWithTotal(120_000_000m);

            var act = () => _sut.GetCheckoutSummaryAsync(_customer.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Đơn hàng trên*vui lòng liên hệ NV Bán hàng để nhận báo giá B2B.");
        }

        // L1-ORD-06 | EP-Invalid | Đơn 80M dù khách có giá thỏa thuận -> vẫn dùng bậc chiết khấu thường (8%)
        [Fact]
        public async Task L1_ORD_06_Checkout_80M_NegotiatedPriceNotApplied()
        {
            _profile.SavedB2BPriceSnapshot = "{\"negotiated\":true}";
            _db.SaveChanges();
            SeedCartWithTotal(80_000_000m);

            var summary = await _sut.GetCheckoutSummaryAsync(_customer.Id);

            summary.DiscountPercentage.Should().Be(8); // giá thỏa thuận chỉ dành cho >= 100M
        }

        //  ▶ Block: PlaceOrderAsync()

        // L1-ORD-07 | EP-Valid | Checkout COD hợp lệ -> đơn 'Chờ xác nhận', giỏ được xóa, Sale phụ trách nhận thông báo
        [Fact]
        public async Task L1_ORD_07_PlaceOrder_Valid_OrderCreatedAndSalesNotified()
        {
            SeedCartWithTotal(5_000_000m, qty: 2);

            var response = await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            var order = _db.Orders.Single(o => o.Id == response.OrderId);
            order.OrderStatus.Should().Be(OrderStatus.PendingConfirmation); // 'Order Received'
            order.OrderItems.Should().NotBeEmpty();
            _db.CartItems.Count().Should().Be(0); // giỏ đã được xóa
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_02_NewOrder, _salesStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once);
        }

        // L1-ORD-08 | EP-Invalid | Giỏ trống -> chặn checkout, không tạo đơn
        [Fact]
        public async Task L1_ORD_08_PlaceOrder_EmptyCart_Rejected()
        {
            var act = () => _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            await act.Should().ThrowAsync<Exception>().WithMessage("Giỏ hàng trống.");
            _db.Orders.Count().Should().Be(0);
        }

        // L1-ORD-09 | SKIP/Blocked — spec yêu cầu CHẶN checkout khi giỏ quá 24h; hành vi thật:
        // CartService tự làm mới giá về giá niêm yết hiện tại rồi cho checkout tiếp.
        [Fact(Skip = "Blocked: không có logic chặn checkout khi giá hết hạn — CartService tự refresh giá (xem L1-CART-03). Ghi Notes.")]
        public void L1_ORD_09_PlaceOrder_PriceExpiredCart_Blocked() { }

        // L1-ORD-10 | Guard-TRUE | Hết tồn kho tại thời điểm checkout -> chặn, không tạo đơn, giỏ giữ nguyên
        // (PlaceOrderAsync có gọi ReserveAsync giữ mềm tồn kho — trước đây bị che khuất do test dùng
        // mock IInventoryReservationService rỗng, không mô phỏng đúng việc kiểm tra tồn kho thật.)
        [Fact]
        public async Task L1_ORD_10_PlaceOrder_OutOfStockItem_Blocked()
        {
            SeedCartWithTotal(5_000_000m, qty: 5, stock: 2); // giỏ cần 5, kho chỉ còn 2

            var act = () => _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            await act.Should().ThrowAsync<Exception>();
            _db.Orders.Count().Should().Be(0);
            _db.CartItems.Count().Should().Be(1); // giỏ không bị xóa khi checkout thất bại
        }

        // L1-ORD-11 | CC | Server tự tính tiền từ giỏ — client không thể gửi tổng tiền giả mạo
        [Fact]
        public async Task L1_ORD_11_PlaceOrder_ServerSideCalculationAuthoritative()
        {
            SeedCartWithTotal(5_000_000m); // request DTO KHÔNG có trường tổng tiền -> không thể tamper

            var response = await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            response.FinalPayment.Should().Be(5_000_000m); // đúng giá server tính từ snapshot giỏ
            _db.Orders.Single().TotalAmount.Should().Be(5_000_000m);
        }

        // L1-ORD-11b | EP-Valid | Khách có MST -> VAT áp dụng ở PlaceOrder khớp với preview (checkout-summary),
        // KHÔNG phụ thuộc cờ request.RequiresRedInvoice (client không gửi vẫn phải tính đúng VAT)
        [Fact]
        public async Task L1_ORD_11b_PlaceOrder_CustomerWithTaxCode_VatMatchesPreview()
        {
            _profile.TaxCode = "0312345678";
            _db.SaveChanges();
            SeedCartWithTotal(5_000_000m);

            var preview = await _sut.GetCheckoutSummaryAsync(_customer.Id);
            var response = await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            preview.VatAmount.Should().BeGreaterThan(0);
            var order = _db.Orders.Single(o => o.Id == response.OrderId);
            order.VatAmount.Should().Be(preview.VatAmount);
            order.FinalPayment.Should().Be(preview.FinalPayment);
        }

        //  ▶ Block: GenerateSePayQrAsync()

        // L1-ORD-12 | State-Valid | Sinh QR SePay -> URL chứa STK + số tiền + mã đơn làm nội dung CK
        [Fact]
        public async Task L1_ORD_12_GenerateSePayQr_UrlAndRefCode()
        {
            var order = SeedOrder(o => o.FinalPayment = 5_000_000m);

            var qr = await _sut.GenerateSePayQrAsync(order.Id, _customer.Id, "Customer");

            qr.QrImageUrl.Should().Contain("acc=0123456789");
            qr.QrImageUrl.Should().Contain("amount=5000000");
            qr.QrImageUrl.Should().Contain($"des={order.OrderCode}");
            qr.TransferContent.Should().Be(order.OrderCode); // mã đối soát duy nhất
        }

        // L1-ORD-13 | State-Invalid | Đơn đã Paid -> chặn sinh QR mới. ĐÃ SỬA (trùng L1-REG-07).
        [Fact]
        public async Task L1_ORD_13_GenerateSePayQr_AlreadyPaid_Blocked()
        {
            var order = SeedOrder(o => { o.FinalPayment = 5_000_000m; o.PaymentStatus = PaymentStatus.Paid; });

            var act = () => _sut.GenerateSePayQrAsync(order.Id, _customer.Id, "Customer");

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.PaymentTransactions.Count().Should().Be(0);
        }

        //  ▶ Block: ProcessSePayWebhookAsync()

        // L1-ORD-14 | Guard-TRUE | Token đúng + đủ tiền -> Paid, ghi PaymentTransaction, báo Sale (SYS-05)
        [Fact]
        public async Task L1_ORD_14_Webhook_ValidTokenAndAmount_Paid()
        {
            var order = SeedOrder(o => { o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 5_000_000m; });

            await _sut.ProcessSePayWebhookAsync(WebhookPayload(order.OrderCode, 5_000_000m), TestConfig.SePayApiToken);

            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Paid);
            _db.PaymentTransactions.Should().ContainSingle(t => t.OrderId == order.Id && t.IsSuccess);
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_05_SePaySuccess, _salesStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once);
        }

        // L1-ORD-15 | Guard-FALSE | Token sai -> Unauthorized, trạng thái không đổi, không ghi transaction
        [Fact]
        public async Task L1_ORD_15_Webhook_InvalidToken_Rejected()
        {
            var order = SeedOrder(o => { o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 5_000_000m; });

            var act = () => _sut.ProcessSePayWebhookAsync(WebhookPayload(order.OrderCode, 5_000_000m), "WRONG");

            await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Token không hợp lệ.");
            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
            _db.PaymentTransactions.Count().Should().Be(0);
        }

        // L1-ORD-16 | BVA-Max+1 | Chuyển THỪA 1đ -> ĐÃ SỬA: không còn tự ghi nhận Paid (trước đây
        // code dùng `<` nên `>=` lọt qua). FT-03 BV-02 yêu cầu khớp chính xác từng đồng.
        // Case này nay trùng với L1-ORD-75 (bộ 3 mốc −1/khớp/+1 chính thức của doc v2.3) — giữ lại
        // để không phá số ID cũ, nhưng đã cập nhật theo hành vi ĐÚNG.
        [Fact]
        public async Task L1_ORD_16_Webhook_Overpay1Dong_RejectedForReconciliation()
        {
            var order = SeedOrder(o => { o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 5_000_000m; });

            await _sut.ProcessSePayWebhookAsync(WebhookPayload(order.OrderCode, 5_000_001m), TestConfig.SePayApiToken);

            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
            _db.PaymentExceptions.Should().NotBeEmpty("khoản chênh lệch thừa phải vào danh sách đối soát");
        }

        // L1-ORD-17 | BVA-Min-1 | Chuyển THIẾU 1đ -> không ghi nhận thanh toán, trạng thái không đổi
        [Fact]
        public async Task L1_ORD_17_Webhook_Underpay1Dong_NotApplied()
        {
            var order = SeedOrder(o => { o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 5_000_000m; });

            await _sut.ProcessSePayWebhookAsync(WebhookPayload(order.OrderCode, 4_999_999m), TestConfig.SePayApiToken);

            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
            _db.PaymentTransactions.Count().Should().Be(0);
        }

        // L1-ORD-18 | EP-Valid | Webhook lặp lại trên đơn đã Paid -> idempotent, không ghi trùng
        [Fact]
        public async Task L1_ORD_18_Webhook_Duplicate_Idempotent()
        {
            var order = SeedOrder(o => { o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 5_000_000m; });
            var payload = WebhookPayload(order.OrderCode, 5_000_000m);
            await _sut.ProcessSePayWebhookAsync(payload, TestConfig.SePayApiToken); // lần 1 -> Paid

            await _sut.ProcessSePayWebhookAsync(payload, TestConfig.SePayApiToken); // lần 2 (duplicate)

            _db.PaymentTransactions.Count(t => t.OrderId == order.Id).Should().Be(1); // không double-apply
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_05_SePaySuccess, It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()), Times.Once);
        }

        // L1-ORD-19 | Guard-FALSE | Webhook đến sau khi đơn đã Cancelled (hết hạn giữ chỗ 15') ->
        // KHÔNG áp dụng thanh toán (PaymentStatus giữ nguyên, không ghi PaymentTransaction),
        // chỉ mở PaymentException để nhân viên đối soát/hoàn tiền thủ công.
        [Fact]
        public async Task L1_ORD_19_Webhook_AfterExpiry_NotApplied()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentStatus = PaymentStatus.Pending;
                o.OrderStatus = OrderStatus.Cancelled;
                o.FinalPayment = 5_000_000m;
            });

            await _sut.ProcessSePayWebhookAsync(WebhookPayload(order.OrderCode, 5_000_000m), TestConfig.SePayApiToken);

            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Cancelled);
            _db.PaymentTransactions.Count(t => t.OrderId == order.Id).Should().Be(0);
            _db.PaymentExceptions.Should().ContainSingle(pe => pe.OrderId == order.Id && pe.ReasonCode == "PAID_AFTER_CANCELLATION");
        }

        //  ▶ Block: ConfirmOrderAsync()

        // L1-ORD-20 | State-Valid | Đơn 'Chờ xác nhận' COD -> Confirmed, fulfillment Allocated + tạo PickTask cho kho
        // (Code mới: ConfirmOrderAsync sinh phiếu soạn hàng (PickTask) tại kho WH-DEFAULT — khớp spec 'picking ticket'.)
        [Fact]
        public async Task L1_ORD_20_Confirm_PendingCod_Confirmed()
        {
            var (wDefault, _) = TestData.Warehouse(w => w.Code = "WH-DEFAULT");
            _db.Warehouses.Add(wDefault);
            _db.SaveChanges();
            var product = TestData.SeedProduct(_db);
            TestData.SeedInventory(_db, product.Id, 10);
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.PendingConfirmation;
                o.PaymentMethod = PaymentMethod.COD;
                o.FulfillmentStatus = FulfillmentStatus.Reserved;
            });
            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, 2));
            _db.SaveChanges();

            await _sut.ConfirmOrderAsync(order.Id, _salesStaff.Id);

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.OrderStatus.Should().Be(OrderStatus.Confirmed);
            updated.FulfillmentStatus.Should().Be(FulfillmentStatus.Allocated);
            _db.PickTasks.Should().Contain(pt => pt.OrderId == order.Id); // picking ticket được tạo
        }

        // L1-ORD-21 | State-Invalid | Xác nhận đơn đã Cancelled -> conflict, trạng thái không đổi
        [Fact]
        public async Task L1_ORD_21_Confirm_CancelledOrder_Conflict()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.Cancelled);

            var act = () => _sut.ConfirmOrderAsync(order.Id, _salesStaff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Chỉ đơn hàng ở trạng thái 'Mới' hoặc 'Chờ xác nhận' mới có thể được xác nhận.");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Cancelled);
        }

        //  ▶ Block: PlaceDirectOrderAsync() — walk-in

        private (Product product, Inventory inv) SeedDirectOrderStock(int stock)
        {
            var product = TestData.SeedProduct(_db);
            var inv = TestData.SeedInventory(_db, product.Id, stock);
            return (product, inv);
        }

        private static PlaceDirectOrderRequestDto DirectRequest(Guid productId, int qty, string? phone = "0912345678",
            PaymentMethod method = PaymentMethod.Cash) => new()
        {
            CustomerName = "Khách Vãng Lai",
            PhoneNumber = phone,
            TotalAmount = qty * 50_000m,
            FinalPayment = qty * 50_000m,
            PaymentMethod = method,
            Items = new List<DirectOrderItemDto> { new() { ProductId = productId, Quantity = qty, Price = 50_000m } }
        };

        // L1-ORD-22 | EP-Valid | SĐT đã có trong hệ thống -> tái sử dụng hồ sơ, không tạo profile trùng
        [Fact]
        public async Task L1_ORD_22_DirectOrder_KnownPhone_ProfileReused()
        {
            var (product, _) = SeedDirectOrderStock(10);
            var profileCountBefore = _db.CustomerProfiles.Count(); // _customer có phone 0912345678

            var response = await _sut.PlaceDirectOrderAsync(DirectRequest(product.Id, 2, phone: _customer.PhoneNumber));

            _db.CustomerProfiles.Count().Should().Be(profileCountBefore); // không tạo profile mới
            _db.Orders.Single(o => o.Id == response.OrderId).CustomerProfileId.Should().Be(_profile.Id);
        }

        // L1-ORD-23 | EP-Invalid + BVA-Max+1 | Số lượng = tồn + 1 -> chặn, không tạo đơn, tồn kho không đổi
        [Fact]
        public async Task L1_ORD_23_DirectOrder_QtyExceedsStock_Blocked()
        {
            var (product, _) = SeedDirectOrderStock(10);

            var act = () => _sut.PlaceDirectOrderAsync(DirectRequest(product.Id, 11));

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Stock depleted by another transaction*");
            _db.ChangeTracker.Clear();
            _db.Orders.Count().Should().Be(0);
            _db.Inventories.Single(i => i.ProductId == product.Id).OnHandQuantity.Should().Be(10);
        }

        // L1-ORD-24 | EP-Invalid | SĐT sai định dạng ('0912345678a', 9 chữ số) -> chặn, không tạo đơn,
        // không thực hiện tra cứu hồ sơ theo SĐT. ĐÃ SỬA: trước đây BE hoàn toàn không validate.
        [Theory]
        [InlineData("0912345678a")]
        [InlineData("091234567")]
        public async Task L1_ORD_24_DirectOrder_InvalidPhoneFormat_Rejected(string invalidPhone)
        {
            var (product, _) = SeedDirectOrderStock(10);
            var profileCountBefore = _db.CustomerProfiles.Count();

            var act = () => _sut.PlaceDirectOrderAsync(DirectRequest(product.Id, 1, phone: invalidPhone));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Số điện thoại không đúng định dạng.");
            _db.Orders.Count().Should().Be(0);
            _db.CustomerProfiles.Count().Should().Be(profileCountBefore, "không được tra cứu/tạo hồ sơ khi SĐT sai định dạng");
        }

        // L1-ORD-25 | EP-Invalid | Danh sách sản phẩm trống -> chặn, không lưu gì
        [Fact]
        public async Task L1_ORD_25_DirectOrder_EmptyItems_Blocked()
        {
            var request = DirectRequest(Guid.NewGuid(), 1);
            request.Items = new List<DirectOrderItemDto>();

            var act = () => _sut.PlaceDirectOrderAsync(request);

            await act.Should().ThrowAsync<Exception>().WithMessage("Danh sách sản phẩm trống.");
            _db.Orders.Count().Should().Be(0);
        }

        // L1-ORD-25b | EP-Invalid | FinalPayment client gửi không khớp TotalAmount-Discount+Vat -> chặn, không tạo đơn
        [Fact]
        public async Task L1_ORD_25b_DirectOrder_AmountMismatch_Blocked()
        {
            var (product, _) = SeedDirectOrderStock(10);
            var request = DirectRequest(product.Id, 2); // TotalAmount = FinalPayment = 100_000
            request.FinalPayment = 999_000m; // giả mạo số tiền phải trả

            var act = () => _sut.PlaceDirectOrderAsync(request);

            await act.Should().ThrowAsync<Exception>().WithMessage("*FinalPayment*");
            _db.Orders.Count().Should().Be(0);
            _db.Inventories.Single(i => i.ProductId == product.Id).OnHandQuantity.Should().Be(10); // không trừ kho
        }

        // L1-ORD-26 | EP-Valid | Xác nhận đơn walk-in -> đơn + lịch sử mua + trừ kho trong 1 giao dịch
        [Fact]
        public async Task L1_ORD_26_DirectOrder_Atomic_InvoiceHistoryStock()
        {
            var (product, _) = SeedDirectOrderStock(10);

            var response = await _sut.PlaceDirectOrderAsync(DirectRequest(product.Id, 4));

            var order = _db.Orders.Single(o => o.Id == response.OrderId);
            order.IsExternalOrder.Should().BeTrue();
            order.PaymentStatus.Should().Be(PaymentStatus.Paid); // Cash = thu ngay
            _db.OrderItems.Count(oi => oi.OrderId == order.Id).Should().Be(1); // lịch sử mua hàng
            _db.Inventories.Single(i => i.ProductId == product.Id).OnHandQuantity.Should().Be(6); // 10 - 4
        }

        // L1-ORD-27 | BVA-Max | Số lượng = đúng tồn kho -> cho phép, tồn về 0 (không âm)
        [Fact]
        public async Task L1_ORD_27_DirectOrder_QtyEqualsStock_StockToZero()
        {
            var (product, _) = SeedDirectOrderStock(10);

            var response = await _sut.PlaceDirectOrderAsync(DirectRequest(product.Id, 10));

            response.OrderId.Should().NotBeEmpty();
            _db.Inventories.Single(i => i.ProductId == product.Id).OnHandQuantity.Should().Be(0);
        }

        //  ▶ Block: ConfirmDirectOrderPaymentAsync()

        // L1-ORD-27b | State-Valid | Xác nhận thu tiền mặt đơn walk-in COD -> Paid + Completed
        [Fact]
        public async Task L1_ORD_27b_ConfirmDirectOrderPayment_Valid_PaidAndCompleted()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.PaymentStatus = PaymentStatus.Pending;
                o.OrderStatus = OrderStatus.PendingConfirmation;
            });

            await _sut.ConfirmDirectOrderPaymentAsync(order.Id);

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.PaymentStatus.Should().Be(PaymentStatus.Paid);
            updated.OrderStatus.Should().Be(OrderStatus.Completed);
        }

        // L1-ORD-27c | State-Invalid | Xác nhận thu tiền lần 2 trên đơn đã Paid -> conflict
        [Fact]
        public async Task L1_ORD_27c_ConfirmDirectOrderPayment_AlreadyPaid_Rejected()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.PaymentStatus = PaymentStatus.Paid;
                o.OrderStatus = OrderStatus.Completed;
            });

            var act = () => _sut.ConfirmDirectOrderPaymentAsync(order.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // L1-ORD-27d | State-Invalid | Xác nhận thu tiền trên đơn đã Cancelled -> conflict
        [Fact]
        public async Task L1_ORD_27d_ConfirmDirectOrderPayment_Cancelled_Rejected()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.PaymentStatus = PaymentStatus.Pending;
                o.OrderStatus = OrderStatus.Cancelled;
            });

            var act = () => _sut.ConfirmDirectOrderPaymentAsync(order.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.Orders.Single(o => o.Id == order.Id).PaymentStatus.Should().Be(PaymentStatus.Pending);
        }

        //  ▶ Block: RequestVatInvoiceAsync()

        // L1-ORD-28 | BVA-Max+1 | Yêu cầu VAT sau 169h -> chặn vì quá cửa sổ 7 ngày (168h)
        // (Ghi chú: code tính mốc từ CreatedAt của đơn, không phải thời điểm giao hàng.)
        [Fact]
        public async Task L1_ORD_28_RequestVat_After169h_Blocked()
        {
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Completed;
                o.CreatedAt = DateTime.UtcNow.AddHours(-169);
            });

            var act = () => _sut.RequestVatInvoiceAsync(_customer.Id, order.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Đã quá thời hạn 7 ngày để yêu cầu hóa đơn VAT.");
            _db.Orders.Single(o => o.Id == order.Id).RedInvoiceStatus.Should().Be(RedInvoiceStatus.None);
        }

        // L1-ORD-29 | BVA-Max | Yêu cầu VAT tại 167h59m -> chấp nhận, tạo yêu cầu Pending
        // ⚠ TEST NÀY FAIL DO DEFECT THẬT TRONG CODE: RequestVatInvoiceAsync load profile (tracked)
        // rồi Update() lên order lấy từ GetOrderDetailForCustomerAsync (AsNoTracking, include CustomerProfile)
        // -> EF ném InvalidOperationException double-tracking cùng key CustomerProfile.
        // Điền Status = Fail + Defect ID vào Excel. Fix gợi ý: bỏ AsNoTracking hoặc load lại order tracked trước khi update.
        [Fact]
        public async Task L1_ORD_29_RequestVat_Within168h_Accepted()
        {
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Completed;
                o.CreatedAt = DateTime.UtcNow.AddHours(-167).AddMinutes(-59);
            });
            _db.ChangeTracker.Clear();

            await _sut.RequestVatInvoiceAsync(_customer.Id, order.Id);

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.RedInvoiceStatus.Should().Be(RedInvoiceStatus.Pending);
            updated.RequiresRedInvoice.Should().BeTrue();
        }

        // L1-ORD-30 | State-Invalid | Đơn chưa giao xong (Processing) -> từ chối yêu cầu VAT
        [Fact]
        public async Task L1_ORD_30_RequestVat_NotDelivered_Rejected()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.Processing);

            var act = () => _sut.RequestVatInvoiceAsync(_customer.Id, order.Id);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Chỉ đơn hàng đã giao thành công mới được yêu cầu hóa đơn VAT.");
        }

        //  ▶ Block: GetOrderHistoryAsync() / GetOrderDetailForCustomerAsync()

        // L1-ORD-31 | EP-Invalid | Khách xem chi tiết đơn của khách khác -> not-found, không lộ dữ liệu
        [Fact]
        public async Task L1_ORD_31_GetOrderDetail_OtherCustomersOrder_Forbidden()
        {
            var (_, profile9) = TestData.SeedCustomer(_db);
            var order9 = TestData.Order(profile9.Id);
            _db.Orders.Add(order9);
            _db.SaveChanges();

            var act = () => _sut.GetOrderDetailForCustomerAsync(_customer.Id, order9.Id);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Không tìm thấy đơn hàng hoặc bạn không có quyền xem đơn này.");
        }

        // L1-ORD-32 | EP-Valid | Lịch sử chỉ trả đơn của chính người gọi, phân trang đúng TotalCount
        [Fact]
        public async Task L1_ORD_32_GetOrderHistory_OnlyCallersOrders()
        {
            SeedOrder(); SeedOrder(); SeedOrder(); // 3 đơn của U1
            var (_, profile9) = TestData.SeedCustomer(_db);
            _db.Orders.AddRange(TestData.Order(profile9.Id), TestData.Order(profile9.Id)); // 2 đơn của U9
            _db.SaveChanges();

            var result = await _sut.GetOrderHistoryAsync(_customer.Id, new OrderHistoryQueryDto { Page = 1 });

            result.TotalCount.Should().Be(3);
            result.Items.Should().HaveCount(3);
        }

        //  ▶ Block: RejectOrderAsync() / RequestCancelOrderAsync() / ProcessCancelRequestAsync()

        // L1-ORD-33 | State-Valid | Sales từ chối đơn 'Chờ xác nhận' -> đơn đóng (Cancelled)
        // (Ghi chú: code hiện KHÔNG lưu lý do từ chối và không gửi notification — lệch spec.)
        [Fact]
        public async Task L1_ORD_33_RejectOrder_PendingConfirmation_Cancelled()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.PendingConfirmation);

            await _sut.RejectOrderAsync(order.Id, _salesStaff.Id, "hết hàng");

            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Cancelled);
        }

        // L1-ORD-34 | EP-Invalid | Từ chối không kèm lý do -> chặn, trạng thái không đổi.
        // ĐÃ SỬA: trước đây reason bị bỏ qua, không validate, không lưu.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task L1_ORD_34_RejectOrder_WithoutReason_ValidationError(string? reason)
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.PendingConfirmation);

            var act = () => _sut.RejectOrderAsync(order.Id, _salesStaff.Id, reason!);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Vui lòng nhập lý do từ chối đơn hàng.");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.PendingConfirmation);
        }

        // L1-ORD-35 | State-Valid | Khách yêu cầu hủy khi đơn 'Chờ xác nhận' -> CancelRequested + báo Sale, đơn CHƯA hủy
        [Fact]
        public async Task L1_ORD_35_RequestCancel_PendingOrder_Recorded()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.PendingConfirmation);

            await _sut.RequestCancelOrderAsync(order.Id, _customer.Id, new RequestCancelOrderDto { Reason = "Đặt nhầm" });

            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.CancelRequested); // chờ Sales quyết định
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_24_CustomerRequestedCancel, _salesStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once);
        }

        // L1-ORD-36 | State-Invalid | Yêu cầu hủy đơn đã đóng (Completed) -> conflict, không tạo yêu cầu
        // (Ghi chú: guard thực tế theo OrderStatus; trạng thái 'đang giao' (Processing) hiện vẫn cho yêu cầu hủy.)
        [Fact]
        public async Task L1_ORD_36_RequestCancel_ClosedOrder_Rejected()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.Completed);

            var act = () => _sut.RequestCancelOrderAsync(order.Id, _customer.Id, new RequestCancelOrderDto { Reason = "x" });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Không thể yêu cầu hủy đơn hàng ở trạng thái Completed.");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Completed);
        }

        // L1-ORD-37 | State-Valid | Sales duyệt yêu cầu hủy (đơn đã Paid) -> Cancelled + hoàn tiền vào ví Credit
        [Fact]
        public async Task L1_ORD_37_ProcessCancel_Approved_CancelledAndRefundedToCredit()
        {
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.CancelRequested;
                o.PaymentStatus = PaymentStatus.Paid;
                o.FinalPayment = 5_000_000m;
            });

            await _sut.ProcessCancelRequestAsync(order.Id, _salesStaff.Id,
                new VietTien.API.DTOs.Order.ProcessCancelRequestDto { IsApproved = true, Reason = "OK" });

            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Cancelled);
            _db.CustomerProfiles.Single(cp => cp.Id == _profile.Id).AvailableCredit.Should().Be(5_000_000m);
            _db.CreditTransactions.Should().ContainSingle(t => t.OrderId == order.Id && t.Amount == 5_000_000m);
        }

        // L1-ORD-37b | State-Invalid | Duyệt hủy trên đơn KHÔNG ở trạng thái CancelRequested (vd. đã Completed)
        // -> conflict, không hoàn tiền/hoàn kho lần nữa (chặn hoàn tiền trùng nếu bị gọi lại)
        [Fact]
        public async Task L1_ORD_37b_ProcessCancel_ApprovedOnNonCancelRequested_Rejected()
        {
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Completed;
                o.PaymentStatus = PaymentStatus.Paid;
                o.FinalPayment = 5_000_000m;
            });

            var act = () => _sut.ProcessCancelRequestAsync(order.Id, _salesStaff.Id,
                new VietTien.API.DTOs.Order.ProcessCancelRequestDto { IsApproved = true, Reason = "OK" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Completed);
            _db.CustomerProfiles.Single(cp => cp.Id == _profile.Id).AvailableCredit.Should().Be(0m); // không hoàn tiền
            _db.CreditTransactions.Count().Should().Be(0);
        }

        //  ▶ Block: ScheduleDeliveryAsync()

        /// <summary>Code mới: đội xe đọc từ bảng Vehicles (IsActive) — seed 5 xe hoạt động như spec BR-10.</summary>
        private void SeedFleet(int count = 5)
        {
            for (int i = 1; i <= count; i++)
                _db.Vehicles.Add(new Vehicle { VehicleNumber = i, LicensePlate = $"29C-{i:D3}", IsActive = true });
            _db.SaveChanges();
        }

        // L1-ORD-38 | State-Valid | Đơn chưa xếp lịch + xe/ca còn trống -> Scheduled, lưu phân công
        // (Ghi chú: code KHÔNG gửi SMS/Email cho khách ở bước này — lệch spec FT-04 AC-03.)
        [Fact]
        public async Task L1_ORD_38_ScheduleDelivery_Valid_Scheduled()
        {
            SeedFleet();
            var order = SeedOrder(o => o.DeliveryStatus = DeliveryStatus.NotScheduled);

            var result = await _sut.ScheduleDeliveryAsync(_salesStaff.Id, new ScheduleDeliveryRequestDto
            {
                OrderIds = new List<Guid> { order.Id },
                VehicleId = 1,
                Shift = "Sáng",
                DeliveryDate = DateTime.UtcNow.Date.AddDays(1)
            });

            result.OrdersScheduled.Should().Be(1);
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.DeliveryStatus.Should().Be(DeliveryStatus.Scheduled);
            updated.DeliveryVehicleId.Should().Be(1);
            updated.DeliveryShift.Should().Be("Sáng");
        }

        // L1-ORD-39 | EP-Invalid | Xe/ca đang bận (đơn khác InDelivery) -> chặn với thông báo kín lịch
        [Fact]
        public async Task L1_ORD_39_ScheduleDelivery_VehicleBusy_Blocked()
        {
            SeedFleet();
            SeedOrder(o => { o.DeliveryVehicleId = 1; o.DeliveryShift = "Sáng"; o.DeliveryStatus = DeliveryStatus.InDelivery; });
            var order = SeedOrder(o => o.DeliveryStatus = DeliveryStatus.NotScheduled);

            var act = () => _sut.ScheduleDeliveryAsync(_salesStaff.Id, new ScheduleDeliveryRequestDto
            {
                OrderIds = new List<Guid> { order.Id },
                VehicleId = 1,
                Shift = "Sáng",
                DeliveryDate = DateTime.UtcNow.Date.AddDays(1)
            });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Xe 1 đang trong ca Sáng.*");
            _db.Orders.Single(o => o.Id == order.Id).DeliveryStatus.Should().Be(DeliveryStatus.NotScheduled);
        }

        // L1-ORD-40 | BVA-Max / BVA-Max+1 | Xe 5 (cuối đội xe) hợp lệ; xe 6 (ngoài đội) bị từ chối
        // (Code mới: đội xe cấu hình trong bảng Vehicles thay vì hard-code 1-5.)
        [Fact]
        public async Task L1_ORD_40_ScheduleDelivery_Vehicle5Ok_Vehicle6Rejected()
        {
            SeedFleet(5); // đội xe đúng 5 chiếc
            var order = SeedOrder(o => o.DeliveryStatus = DeliveryStatus.NotScheduled);
            var tomorrow = DateTime.UtcNow.Date.AddDays(1);

            // BVA-Max: xe 5 — hợp lệ
            var ok = await _sut.ScheduleDeliveryAsync(_salesStaff.Id, new ScheduleDeliveryRequestDto
            { OrderIds = new List<Guid> { order.Id }, VehicleId = 5, Shift = "Sáng", DeliveryDate = tomorrow });
            ok.OrdersScheduled.Should().Be(1);

            // BVA-Max+1: xe 6 — bị từ chối
            var act = () => _sut.ScheduleDeliveryAsync(_salesStaff.Id, new ScheduleDeliveryRequestDto
            { OrderIds = new List<Guid> { order.Id }, VehicleId = 6, Shift = "Sáng", DeliveryDate = tomorrow });
            await act.Should().ThrowAsync<Exception>().WithMessage("Mã xe không hợp lệ hoặc xe đã ngừng hoạt động.");
        }

        // L1-ORD-41 | EP-Invalid | Ca ngoài {Sáng, Trưa, Chiều} -> validation error, không phân công
        [Fact]
        public async Task L1_ORD_41_ScheduleDelivery_InvalidShift_Rejected()
        {
            SeedFleet();
            var order = SeedOrder(o => o.DeliveryStatus = DeliveryStatus.NotScheduled);

            var act = () => _sut.ScheduleDeliveryAsync(_salesStaff.Id, new ScheduleDeliveryRequestDto
            {
                OrderIds = new List<Guid> { order.Id },
                VehicleId = 1,
                Shift = "Tối",
                DeliveryDate = DateTime.UtcNow.Date.AddDays(1)
            });

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Ca giao hàng không hợp lệ. Chọn: Sáng / Trưa / Chiều.");
        }

        //  ▶ Block: RecordDeliveryResultAsync()

        // L1-ORD-42 | State-Valid | COD thu đủ tiền -> Paid + Completed + Delivered, không phát sinh công nợ
        [Fact]
        public async Task L1_ORD_42_RecordDelivery_CodCollectedFull_PaidAndDelivered()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.OrderStatus = OrderStatus.Processing;
                o.DeliveryStatus = DeliveryStatus.InDelivery;
                o.FinalPayment = 5_000_000m;
            });

            var result = await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, new RecordDeliveryResultDto
            {
                DeliveryOutcome = "delivered",
                AmountCollected = 5_000_000m
            });

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.PaymentStatus.Should().Be(PaymentStatus.Paid);
            updated.OrderStatus.Should().Be(OrderStatus.Completed);
            updated.DeliveryStatus.Should().Be(DeliveryStatus.Delivered);
            result.DebtRecordCreated.Should().BeFalse();
            _db.CustomerDebts.Count().Should().Be(0);
        }

        // L1-ORD-43 | EP-Invalid | Đơn đã thanh toán qua SePay -> chặn thu COD, không thu tiền lần 2.
        // ĐÃ SỬA: trước đây không có guard 'PAID - DO NOT COLLECT'.
        [Fact]
        public async Task L1_ORD_43_RecordDelivery_AlreadyPaidSePay_CollectionBlocked()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = PaymentStatus.Paid;
                o.OrderStatus = OrderStatus.Processing;
                o.DeliveryStatus = DeliveryStatus.InDelivery;
                o.FinalPayment = 5_000_000m;
            });

            var act = () => _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, new RecordDeliveryResultDto
            {
                DeliveryOutcome = "delivered",
                AmountCollected = 5_000_000m
            });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Đơn hàng đã được thanh toán qua SePay, không được thu thêm tiền mặt (COD).");
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.PaymentStatus.Should().Be(PaymentStatus.Paid);
            updated.DeliveryStatus.Should().Be(DeliveryStatus.InDelivery);
        }

        // L1-ORD-44 | State-Invalid | Khách từ chối nhận hàng nhưng KHÔNG kèm reason code -> chặn.
        // ĐÃ SỬA: trước đây DTO không có khái niệm reason code bắt buộc.
        [Fact]
        public async Task L1_ORD_44_RecordDelivery_RejectedWithoutReasonCode_Rejected()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.OrderStatus = OrderStatus.Processing;
                o.DeliveryStatus = DeliveryStatus.InDelivery;
            });

            var act = () => _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, new RecordDeliveryResultDto
            {
                DeliveryOutcome = "failed",
                AmountCollected = 0,
                CustomerRejected = true,
                RejectionReasonCode = null
            });

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Vui lòng chọn lý do khi khách từ chối nhận hàng.");
            _db.Orders.Single(o => o.Id == order.Id).DeliveryStatus.Should().Be(DeliveryStatus.InDelivery);
        }

        // L1-ORD-45 | State-Valid | Giao thất bại -> đếm số lần thất bại, xếp lịch lại; lần 3 thì KHÓA đơn
        // (Ghi chú: spec mô tả 'Awaiting Return + ReturnedGoodsLog' — code thực tế dùng cơ chế Failed/Rescheduled/Blocked.)
        [Fact]
        public async Task L1_ORD_45_RecordDelivery_Failed_RescheduledThenBlockedAt3()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.COD;
                o.OrderStatus = OrderStatus.Processing;
                o.DeliveryStatus = DeliveryStatus.InDelivery;
            });
            var dto = new RecordDeliveryResultDto { DeliveryOutcome = "failed", AmountCollected = 0 };

            await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, dto); // lần 1
            _db.Orders.Single(o => o.Id == order.Id).DeliveryStatus.Should().Be(DeliveryStatus.Rescheduled);

            await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, dto); // lần 2
            var result3 = await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, dto); // lần 3 -> khóa

            result3.IsBlockedByFailures.Should().BeTrue();
            _db.Orders.Single(o => o.Id == order.Id).FailedDeliveryCount.Should().Be(3);
            var act = () => _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, dto); // lần 4 bị chặn
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Đơn hàng đã bị khóa do thất bại quá 3 lần.*");
        }

        //  ▶ Block: RequestCancelPaidOrderAsync() / ApproveCancelAndCreateReplacementAsync()

        // L1-ORD-46 | EP-Valid | Duyệt hủy đơn PAID -> tạo đơn thay thế, tiền thừa vào ví Credit, KHÔNG hoàn tiền mặt
        [Fact]
        public async Task L1_ORD_46_ApproveCancelPaid_ReplacementCreatedNoRefund()
        {
            var product = TestData.SeedProduct(_db);
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.CancelRequested;
                o.PaymentStatus = PaymentStatus.Paid;
                o.FinalPayment = 5_000_000m;
            });

            var result = await _sut.ApproveCancelAndCreateReplacementAsync(order.Id, _salesStaff.Id,
                new CreateReplacementOrderDto
                {
                    OriginalOrderId = order.Id,
                    Items = new List<ReplacementOrderItemDto> { new() { ProductId = product.Id, Quantity = 1, Price = 3_000_000m } }
                });

            var original = _db.Orders.Single(o => o.Id == order.Id);
            original.OrderStatus.Should().Be(OrderStatus.CancelledReallocated); // đơn gốc đóng
            original.ReplacementOrderId.Should().Be(result.ReplacementOrderId); // tham chiếu đơn thay thế
            var replacement = _db.Orders.Single(o => o.Id == result.ReplacementOrderId);
            replacement.PaymentStatus.Should().Be(PaymentStatus.Paid); // tiền đơn gốc gánh sang
            result.CreditAllocated.Should().Be(2_000_000m); // 5M - 3M chuyển vào ví, KHÔNG refund
            _db.CustomerProfiles.Single(cp => cp.Id == _profile.Id).AvailableCredit.Should().Be(2_000_000m);
            _db.PaymentReallocations.Count().Should().Be(2); // ReallocatedToOrder + RefundedToCredit
        }

        // L1-ORD-47 | role gate (chỉ Manager được duyệt) nằm ở controller [Authorize], service không
        // nhận thông tin role để kiểm tra ở unit level -> đã chuyển sang L3:
        // VietTien.IntegrationTests/RoleGateTests.cs (L1_ORD_47_ApproveCancelReplacement_NonManagerRole_Forbidden).
    }
}
