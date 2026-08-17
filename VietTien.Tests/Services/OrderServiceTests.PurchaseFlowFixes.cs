using FluentAssertions;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — sửa luồng mua hàng + POS (VAT bắt buộc, giữ chỗ tồn kho theo giỏ chống
    /// tranh chấp "2 người đặt 1 đơn", hóa đơn đỏ, Hóa đơn PDF chính thức). Dùng chung fixture với
    /// OrderServiceTests (partial class).
    /// </summary>
    public partial class OrderServiceTests
    {
        static OrderServiceTests()
        {
            // Program.cs sets this at app startup, nhưng unit test không chạy qua host startup —
            // phải tự khai báo license 1 lần trước khi gọi GenerateInvoicePdf trong bất kỳ test nào.
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        }

        /// <summary>CartService.AddItemToCartAsync đòi hỏi khách đã có địa chỉ giao hàng — fixture
        /// dùng chung của OrderServiceTests không seed sẵn địa chỉ (các test khác toàn seed cart item
        /// thẳng qua DB, bỏ qua bước này) nên các test gọi thẳng AddItemToCartAsync phải tự thêm.</summary>
        private void EnsureCustomerHasAddress()
        {
            if (!_db.Addresses.Any(a => a.CustomerProfileId == _profile.Id))
            {
                _db.Addresses.Add(new Address { CustomerProfileId = _profile.Id, IsDefault = true });
                _db.SaveChanges();
            }
        }

        // Tái hiện ĐÚNG kịch bản lỗi ban đầu: tồn 100sp, khách A bỏ 60sp vào giỏ (giữ chỗ ngay lúc
        // thêm), sau đó có người mua trực tiếp (POS) 50sp cùng sản phẩm -> phải bị CHẶN (chỉ còn 40
        // khả dụng), KHÔNG được thành công và làm A mất hàng. Trước đây giỏ không giữ chỗ gì cả nên
        // POS luôn thắng bất kể ai "đặt trước".
        [Fact]
        public async Task L1_ORD_PurchaseFlow_01_CartReservation_BlocksConcurrentPosOversell()
        {
            EnsureCustomerHasAddress();
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 50_000m);
            TestData.SeedInventory(_db, product.Id, 100);
            var cartService = new API.Services.Implementations.CartService(
                new API.Repositories.Implementations.UnitOfWork(_db), _db, new FakeInventoryReservationService(_db));

            // Khách A bỏ 60sp vào giỏ -> giữ chỗ ngay.
            await cartService.AddItemToCartAsync(_customer.Id, new API.DTOs.Cart.AddToCartRequestDto { ProductId = product.Id, Quantity = 60 });

            // POS cố bán trực tiếp 50sp cùng sản phẩm -> chỉ còn 40 khả dụng (100 - 60 đã giữ) -> phải chặn.
            var posRequest = new PlaceDirectOrderRequestDto
            {
                CustomerName = "Khách quầy",
                PhoneNumber = "0987000111",
                TotalAmount = 50 * 50_000m,
                VatAmount = 50 * 5_000m,
                FinalPayment = 50 * 55_000m,
                PaymentMethod = PaymentMethod.Cash,
                Items = new List<DirectOrderItemDto> { new() { ProductId = product.Id, Quantity = 50, Price = 50_000m } }
            };

            var act = () => _sut.PlaceDirectOrderAsync(posRequest, _salesStaff.Id);

            await act.Should().ThrowAsync<Exception>();
            _db.Orders.Count(o => o.IsExternalOrder).Should().Be(0); // POS không tạo được đơn

            // Khách A sau đó vẫn đặt được 60sp đã giữ chỗ trước đó bình thường.
            var response = await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });
            response.OrderId.Should().NotBeEmpty();
        }

        // PlaceOrderAsync "nhả rồi giữ lại" không làm sai lệch tồn kho khi giỏ ĐÃ được giữ chỗ từ
        // lúc thêm vào giỏ — Inventory.ReservedQuantity phải chuyển gọn từ giỏ sang đơn, không cộng dồn.
        [Fact]
        public async Task L1_ORD_PurchaseFlow_02_PlaceOrder_HandoffFromCartReservation_NoDoubleCount()
        {
            EnsureCustomerHasAddress();
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 50_000m);
            var inv = TestData.SeedInventory(_db, product.Id, 100);
            var cartService = new API.Services.Implementations.CartService(
                new API.Repositories.Implementations.UnitOfWork(_db), _db, new FakeInventoryReservationService(_db));
            await cartService.AddItemToCartAsync(_customer.Id, new API.DTOs.Cart.AddToCartRequestDto { ProductId = product.Id, Quantity = 30 });

            _db.Inventories.Single(i => i.Id == inv.Id).ReservedQuantity.Should().Be(30); // giữ chỗ từ lúc thêm giỏ

            await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            // Sau khi đặt hàng: vẫn đúng 30 đang giữ (chuyển từ "giỏ" sang "đơn"), không nhân đôi thành 60.
            _db.Inventories.Single(i => i.Id == inv.Id).ReservedQuantity.Should().Be(30);
        }

        // Giỏ hàng "kiểu cũ" (item được tạo thẳng trong DB, không qua AddItemToCartAsync — mô phỏng dữ
        // liệu tồn tại từ TRƯỚC khi tính năng giữ chỗ theo giỏ lên production, chưa từng có
        // ReservedQuantity nào backing nó) vẫn đặt hàng bình thường được, không bị vỡ vì "nhả" 0 là no-op.
        [Fact]
        public async Task L1_ORD_PurchaseFlow_03_PlaceOrder_LegacyCartWithoutPriorReservation_StillWorks()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 50_000m);
            var inv = TestData.SeedInventory(_db, product.Id, 100);
            var cart = TestData.Cart(_profile.Id);
            _db.Carts.Add(cart);
            _db.CartItems.Add(TestData.CartItem(cart.Id, product.Id, 10, 50_000m)); // KHÔNG qua CartService
            _db.SaveChanges();

            var response = await _sut.PlaceOrderAsync(_customer.Id, new PlaceOrderRequestDto { PaymentMethod = PaymentMethod.COD });

            response.OrderId.Should().NotBeEmpty();
            // "Nhả 0 rồi giữ lại 10" (giỏ cũ chưa từng có giữ chỗ) hoạt động y hệt hành vi ReserveAsync
            // gốc trước đây — vẫn giữ Reserved (chưa Allocated, việc đó diễn ra ở bước xác nhận riêng
            // sau này), không rơi về 0.
            _db.Inventories.Single(i => i.Id == inv.Id).ReservedQuantity.Should().Be(10);
        }

        // ▶ SubmitRedInvoiceAsync — Sale nhập lại thông tin hóa đơn đỏ thật lấy từ bên thứ 3.

        [Fact]
        public async Task L1_ORD_PurchaseFlow_04_SubmitRedInvoice_NotRequired_Throws()
        {
            var order = SeedOrder(o => o.RequiresRedInvoice = false);

            var act = () => _sut.SubmitRedInvoiceAsync(order.Id, _salesStaff.Id, "SalesStaff",
                new SubmitRedInvoiceRequestDto { RedInvoiceNumber = "0001234", RedInvoiceIssuedAt = DateTime.UtcNow });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task L1_ORD_PurchaseFlow_05_SubmitRedInvoice_Success_SetsFieldsAndStatusIssued()
        {
            var order = SeedOrder(o => o.RequiresRedInvoice = true);
            var issuedAt = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

            var result = await _sut.SubmitRedInvoiceAsync(order.Id, _salesStaff.Id, "SalesStaff",
                new SubmitRedInvoiceRequestDto { RedInvoiceNumber = "0001234", RedInvoiceIssuedAt = issuedAt });

            result.RedInvoiceStatus.Should().Be(nameof(RedInvoiceStatus.Issued));
            result.RedInvoiceNumber.Should().Be("0001234");
            var saved = _db.Orders.Single(o => o.Id == order.Id);
            saved.RedInvoiceStatus.Should().Be(RedInvoiceStatus.Issued);
            saved.RedInvoiceNumber.Should().Be("0001234");
            saved.RedInvoiceEnteredByUserId.Should().Be(_salesStaff.Id);
        }

        [Fact]
        public async Task L1_ORD_PurchaseFlow_06_SubmitRedInvoice_CalledTwice_UpdatesExistingValues()
        {
            var order = SeedOrder(o => o.RequiresRedInvoice = true);
            await _sut.SubmitRedInvoiceAsync(order.Id, _salesStaff.Id, "SalesStaff",
                new SubmitRedInvoiceRequestDto { RedInvoiceNumber = "0001234", RedInvoiceIssuedAt = DateTime.UtcNow });

            var result = await _sut.SubmitRedInvoiceAsync(order.Id, _salesStaff.Id, "SalesStaff",
                new SubmitRedInvoiceRequestDto { RedInvoiceNumber = "0009999", RedInvoiceIssuedAt = DateTime.UtcNow });

            result.RedInvoiceNumber.Should().Be("0009999"); // sửa được, không khoá cứng sau khi Issued
        }

        // ▶ GenerateInvoicePdfAsync — Hóa đơn PDF chính thức sinh phía server.

        [Fact]
        public async Task L1_ORD_PurchaseFlow_07_GenerateInvoicePdf_ProducesNonEmptyPdf()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = SeedOrder(o =>
            {
                o.TotalAmount = 200_000m;
                o.VatAmount = 20_000m;
                o.FinalPayment = 220_000m;
            });
            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, 2, 100_000m));
            _db.SaveChanges();

            var pdfBytes = await _sut.GenerateInvoicePdfAsync(order.Id, _customer.Id, "Customer");

            pdfBytes.Should().NotBeNull();
            pdfBytes.Length.Should().BeGreaterThan(0);
            // Chữ ký PDF chuẩn (magic bytes "%PDF") — xác nhận đây thực sự là 1 file PDF hợp lệ.
            System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 4).Should().Be("%PDF");
        }

        [Fact]
        public async Task L1_ORD_PurchaseFlow_08_GenerateInvoicePdf_OtherCustomer_Forbidden()
        {
            var order = SeedOrder();
            var (otherUser, _) = TestData.SeedCustomer(_db);

            var act = () => _sut.GenerateInvoicePdfAsync(order.Id, otherUser.Id, "Customer");

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
