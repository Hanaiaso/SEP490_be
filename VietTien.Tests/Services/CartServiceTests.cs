using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using VietTien.API.Data;
using VietTien.API.DTOs.Cart;
using VietTien.API.Models;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: CartService — L1-CART-01..10. UnitOfWork thật + EF InMemory.
    /// Lưu ý lệch spec (ghi vào Notes của Excel):
    /// - Code không có cờ IsPriceExpired: khi giỏ quá 24h, giá được TỰ ĐỘNG làm mới về giá niêm yết hiện tại.
    /// - Boundary dùng "> 24" nên đúng 24h00 CHƯA coi là hết hạn (spec nói 24h00 = expired).
    /// - Validate quantity > 0 nằm ở DataAnnotation của AddToCartRequestDto (model binding), không nằm trong service.
    /// </summary>
    public class CartServiceTests
    {
        private readonly ApplicationDbContext _db;
        private readonly CartService _sut;

        public CartServiceTests()
        {
            _db = TestDbFactory.Create();
            _sut = new CartService(new UnitOfWork(_db), _db);
        }

        private (User user, CustomerProfile profile) SeedCustomerWithAddress()
        {
            var (user, profile) = TestData.SeedCustomer(_db);
            _db.Addresses.Add(new Address { CustomerProfileId = profile.Id, IsDefault = true });
            _db.SaveChanges();
            return (user, profile);
        }

        private Cart SeedCart(Guid profileId, params (Product product, int qty, decimal unitPrice)[] items)
        {
            var cart = TestData.Cart(profileId);
            _db.Carts.Add(cart);
            foreach (var (product, qty, price) in items)
                _db.CartItems.Add(TestData.CartItem(cart.Id, product.Id, qty, price));
            _db.SaveChanges();
            return cart;
        }

        //  ▶ Block: GetCartAsync()

        // L1-CART-01 | EP-Valid | Giỏ có sẵn 2 item (qty 3 và 5) -> trả đúng item và số lượng
        [Fact]
        public async Task L1_CART_01_GetCart_ExistingCart_ItemsRestored()
        {
            var (user, profile) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            SeedCart(profile.Id, (p1, 3, 50_000m), (p2, 5, 50_000m));

            var cart = await _sut.GetCartAsync(user.Id);

            cart.Items.Should().HaveCount(2);
            cart.Items.Select(i => i.Quantity).Should().BeEquivalentTo(new[] { 3, 5 });
            cart.TotalItems.Should().Be(8);
        }

        // L1-CART-02 | EP-Valid | User chưa có giỏ -> trả giỏ rỗng, không exception
        [Fact]
        public async Task L1_CART_02_GetCart_NoCart_ReturnsEmpty()
        {
            var (user, _) = SeedCustomerWithAddress();

            var cart = await _sut.GetCartAsync(user.Id);

            cart.Items.Should().BeEmpty();
            cart.TotalItems.Should().Be(0);
            cart.TotalPrice.Should().Be(0);
        }

        // L1-CART-03 | BVA-Max | Giỏ quá 24h (25h) -> giá tự làm mới về giá niêm yết hiện tại
        // (Lệch spec: không có cờ IsPriceExpired; hành vi thật = auto refresh giá. Đúng 24h00 chưa expired vì code dùng "> 24".)
        [Fact]
        public async Task L1_CART_03_GetCart_Idle25h_PricesRefreshedToCurrentList()
        {
            var (user, profile) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db, p => p.StandardListedPrice = 60_000m); // giá hiện tại 60k
            var cart = SeedCart(profile.Id, (p1, 2, 50_000m));                        // snapshot cũ 50k
            cart.UpdatedAt = DateTime.UtcNow.AddHours(-25);
            _db.SaveChanges();

            var dto = await _sut.GetCartAsync(user.Id);

            dto.Items.Single().UnitPrice.Should().Be(60_000m); // giá đã refresh
            dto.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10)); // đồng hồ giữ giá reset
        }

        // L1-CART-04 | BVA-Max-1 | Giỏ mới 23h59m -> giá snapshot GIỮ NGUYÊN dù giá niêm yết đã đổi
        [Fact]
        public async Task L1_CART_04_GetCart_Idle23h59_PricesStillLocked()
        {
            var (user, profile) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db, p => p.StandardListedPrice = 60_000m);
            var cart = SeedCart(profile.Id, (p1, 2, 50_000m));
            cart.UpdatedAt = DateTime.UtcNow.AddHours(-23).AddMinutes(-59);
            _db.SaveChanges();

            var dto = await _sut.GetCartAsync(user.Id);

            dto.Items.Single().UnitPrice.Should().Be(50_000m); // vẫn là snapshot
        }

        //  ▶ Block: AddItemToCartAsync()

        // L1-CART-05 | EP-Valid | Thêm sản phẩm mới -> tạo line item với snapshot giá niêm yết
        [Fact]
        public async Task L1_CART_05_AddItem_NewProduct_SnapshotPrice()
        {
            var (user, _) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db, p => p.StandardListedPrice = 50_000m);

            var dto = await _sut.AddItemToCartAsync(user.Id, new AddToCartRequestDto { ProductId = p1.Id, Quantity = 2 });

            dto.Items.Should().ContainSingle(i => i.ProductId == p1.Id && i.Quantity == 2 && i.UnitPrice == 50_000m);
        }

        // L1-CART-06 | EP-Valid | Thêm sản phẩm đã có trong giỏ -> cộng dồn số lượng, không tạo line trùng
        [Fact]
        public async Task L1_CART_06_AddItem_ExistingProduct_QuantityAccumulated()
        {
            var (user, _) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db);
            await _sut.AddItemToCartAsync(user.Id, new AddToCartRequestDto { ProductId = p1.Id, Quantity = 2 });

            var dto = await _sut.AddItemToCartAsync(user.Id, new AddToCartRequestDto { ProductId = p1.Id, Quantity = 3 });

            dto.Items.Should().ContainSingle(i => i.ProductId == p1.Id);
            dto.Items.Single(i => i.ProductId == p1.Id).Quantity.Should().Be(5);
        }

        // L1-CART-07 | EP-Invalid | Quantity <= 0 -> bị chặn bởi [Range(1, ...)] trên AddToCartRequestDto
        // (Lệch spec: service không tự validate; rule nằm ở DataAnnotation, ASP.NET model binding trả 400 trước khi vào service.)
        [Fact]
        public void L1_CART_07_AddItem_QuantityZero_RejectedByValidation()
        {
            var dto = new AddToCartRequestDto { ProductId = Guid.NewGuid(), Quantity = 0 };

            var results = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

            isValid.Should().BeFalse();
            results.Should().Contain(r => r.ErrorMessage == "Số lượng phải lớn hơn 0");
        }

        // L1-CART-08 | EP-Invalid | Sản phẩm Discontinued -> không thêm được, giỏ không đổi
        [Fact]
        public async Task L1_CART_08_AddItem_DiscontinuedProduct_Rejected()
        {
            var (user, profile) = SeedCustomerWithAddress();
            var p2 = TestData.SeedProduct(_db, p => p.IsDiscontinued = true);

            var act = () => _sut.AddItemToCartAsync(user.Id, new AddToCartRequestDto { ProductId = p2.Id, Quantity = 1 });

            await act.Should().ThrowAsync<Exception>().WithMessage("Product not found or discontinued.");
            _db.CartItems.Count().Should().Be(0);
        }

        //  ▶ Block: UpdateCartItemAsync() / ClearCartAsync()

        // L1-CART-09 | EP-Invalid | Update cart item của user khác -> từ chối, số lượng không đổi
        [Fact]
        public async Task L1_CART_09_UpdateItem_OfAnotherUser_Rejected()
        {
            var (user1, _) = SeedCustomerWithAddress();
            var (_, profile9) = TestData.SeedCustomer(_db); // user khác
            var p1 = TestData.SeedProduct(_db);
            SeedCart(profile9.Id, (p1, 2, 50_000m));
            var ci9 = _db.CartItems.Single();

            var act = () => _sut.UpdateCartItemAsync(user1.Id, ci9.Id, new UpdateCartItemRequestDto { Quantity = 10 });

            await act.Should().ThrowAsync<Exception>().WithMessage("Cart item not found.");
            _db.CartItems.Single().Quantity.Should().Be(2);
        }

        // L1-CART-10 | EP-Valid | Clear cart -> mọi item bị xóa, bản ghi giỏ vẫn còn
        [Fact]
        public async Task L1_CART_10_ClearCart_AllItemsRemoved_CartRetained()
        {
            var (user, profile) = SeedCustomerWithAddress();
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            var p3 = TestData.SeedProduct(_db);
            var cart = SeedCart(profile.Id, (p1, 1, 50_000m), (p2, 2, 50_000m), (p3, 3, 50_000m));

            var dto = await _sut.ClearCartAsync(user.Id);

            dto.Items.Should().BeEmpty();
            _db.Carts.Count(c => c.Id == cart.Id).Should().Be(1); // giỏ vẫn tồn tại
            _db.CartItems.Count().Should().Be(0);
        }
    }
}
