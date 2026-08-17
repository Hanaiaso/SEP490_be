using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.ProductPriceUpdate;
using VietTien.API.Models;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: ProductPriceUpdateService — luồng CEO đề xuất -> Sales Manager phân công + thông báo
    /// khách hàng -> Sales Staff thực hiện đúng ngày hiệu lực. EF InMemory + UnitOfWork thật, mock
    /// INotificationService/IAuditLogService.
    /// </summary>
    public class ProductPriceUpdateServiceTests
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<INotificationService> _noti = new();
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly ProductPriceUpdateService _sut;

        private readonly User _ceo;
        private readonly User _manager;
        private readonly User _staff;

        public ProductPriceUpdateServiceTests()
        {
            _db = TestDbFactory.Create();
            var uow = new UnitOfWork(_db);
            _sut = new ProductPriceUpdateService(uow, _db, _noti.Object, _audit.Object);

            _ceo = TestData.User(u => u.Role = SystemRole.CEO);
            _manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _staff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.AddRange(_ceo, _manager, _staff);
            _db.SaveChanges();
        }

        private CreateProductPriceUpdateOrderRequest ProposeRequest(Guid productId, decimal newPrice, DateTime? scheduledDate = null) =>
            new()
            {
                ScheduledEffectiveDate = scheduledDate ?? DateTime.UtcNow.AddDays(1),
                ProposalNote = "Điều chỉnh giá theo thị trường",
                Items = new List<ProductPriceUpdateOrderItemRequest>
                {
                    new() { ProductId = productId, NewPrice = newPrice }
                }
            };

        // ▶ ProposeAsync

        [Fact]
        public async Task Propose_Success_SnapshotsOldPriceAndNotifiesManager()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);

            var dto = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));

            dto.Status.Should().Be("Proposed");
            dto.Items.Single().OldPrice.Should().Be(100_000m);
            dto.Items.Single().NewPrice.Should().Be(98_000m);
            _noti.Verify(n => n.CreateRoleNotificationAsync(
                API.Models.NotificationType.SYS_44_ProductPriceUpdateOrderProposed, SystemRole.SalesManager,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
        }

        [Fact]
        public async Task Propose_DuplicateProductInRequest_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var request = new CreateProductPriceUpdateOrderRequest
            {
                ScheduledEffectiveDate = DateTime.UtcNow.AddDays(1),
                Items = new List<ProductPriceUpdateOrderItemRequest>
                {
                    new() { ProductId = product.Id, NewPrice = 90_000m },
                    new() { ProductId = product.Id, NewPrice = 95_000m },
                }
            };

            var act = () => _sut.ProposeAsync(_ceo.Id, request);

            await act.Should().ThrowAsync<Exception>().WithMessage("*trùng*");
        }

        [Fact]
        public async Task Propose_NewPriceEqualsCurrentPrice_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);

            var act = () => _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 100_000m));

            await act.Should().ThrowAsync<Exception>().WithMessage("*trùng với giá hiện tại*");
        }

        [Fact]
        public async Task Propose_ProductAlreadyInOpenOrder_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));

            var act = () => _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 95_000m));

            await act.Should().ThrowAsync<Exception>().WithMessage("*đợt cập nhật giá khác*");
        }

        // ▶ AssignAndNotifyAsync

        [Fact]
        public async Task AssignAndNotify_Success_SetsNotifiedAndNotifiesStaffAndAffectedCustomers()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));

            var (customerUser, customerProfile) = TestData.SeedCustomer(_db);
            var cart = TestData.Cart(customerProfile.Id);
            _db.Carts.Add(cart);
            _db.CartItems.Add(TestData.CartItem(cart.Id, product.Id, 2, 100_000m));
            _db.SaveChanges();

            var dto = await _sut.AssignAndNotifyAsync(order.Id, _manager.Id, new AssignPriceUpdateOrderRequest { StaffId = _staff.Id });

            dto.Status.Should().Be("Notified");
            dto.AssignedSalesStaffId.Should().Be(_staff.Id);
            _noti.Verify(n => n.CreateNotificationAsync(
                API.Models.NotificationType.SYS_45_ProductPriceUpdateOrderAssigned, _staff.Id,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
            _noti.Verify(n => n.CreateNotificationAsync(
                API.Models.NotificationType.SYS_46_ProductPriceUpdateScheduleNotice, customerUser.Id,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()), Times.Once);
        }

        [Fact]
        public async Task AssignAndNotify_AlreadyNotified_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));
            await _sut.AssignAndNotifyAsync(order.Id, _manager.Id, new AssignPriceUpdateOrderRequest { StaffId = _staff.Id });

            var act = () => _sut.AssignAndNotifyAsync(order.Id, _manager.Id, new AssignPriceUpdateOrderRequest { StaffId = _staff.Id });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task AssignAndNotify_InactiveStaff_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));
            var inactiveStaff = TestData.User(u => { u.Role = SystemRole.SalesStaff; u.IsActive = false; });
            _db.Users.Add(inactiveStaff);
            _db.SaveChanges();

            var act = () => _sut.AssignAndNotifyAsync(order.Id, _manager.Id, new AssignPriceUpdateOrderRequest { StaffId = inactiveStaff.Id });

            await act.Should().ThrowAsync<Exception>().WithMessage("*bị khóa*");
        }

        // ▶ ExecuteAsync

        private async Task<ProductPriceUpdateOrderDto> ProposeAndAssign(Guid productId, decimal newPrice, DateTime scheduledDate)
        {
            var order = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(productId, newPrice, scheduledDate));
            return await _sut.AssignAndNotifyAsync(order.Id, _manager.Id, new AssignPriceUpdateOrderRequest { StaffId = _staff.Id });
        }

        [Fact]
        public async Task Execute_Success_AppliesNewPriceAndLocksCartItems()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddHours(7).Date); // hiệu lực hôm nay

            var (_, customerProfile) = TestData.SeedCustomer(_db);
            var cart = TestData.Cart(customerProfile.Id);
            _db.Carts.Add(cart);
            _db.CartItems.Add(TestData.CartItem(cart.Id, product.Id, 2, 100_000m));
            _db.SaveChanges();

            var dto = await _sut.ExecuteAsync(order.Id, _staff.Id);

            dto.Status.Should().Be("Executed");
            _db.Products.Single(p => p.Id == product.Id).StandardListedPrice.Should().Be(98_000m);
            _db.CartItems.Single(ci => ci.ProductId == product.Id).PriceLockedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task Execute_BeforeScheduledDate_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddDays(5));

            var act = () => _sut.ExecuteAsync(order.Id, _staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Chưa đến ngày*");
        }

        [Fact]
        public async Task Execute_WrongStaff_ThrowsUnauthorized()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddHours(7).Date);
            var otherStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherStaff);
            _db.SaveChanges();

            var act = () => _sut.ExecuteAsync(order.Id, otherStaff.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task Execute_PriceDriftedSincePropose_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddHours(7).Date);

            // Giá bị 1 tác vụ khác đổi sau khi đề xuất, trước khi thực hiện.
            var trackedProduct = _db.Products.Single(p => p.Id == product.Id);
            trackedProduct.StandardListedPrice = 111_000m;
            _db.SaveChanges();

            var act = () => _sut.ExecuteAsync(order.Id, _staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*thay đổi bởi tác vụ khác*");
        }

        // ▶ CancelAsync

        [Fact]
        public async Task Cancel_ManagerCancel_AllowedWhileProposed()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await _sut.ProposeAsync(_ceo.Id, ProposeRequest(product.Id, 98_000m));

            var dto = await _sut.CancelAsync(order.Id, _manager.Id, "SalesManager", new CancelPriceUpdateOrderRequest { Reason = "Đổi ý" });

            dto.Status.Should().Be("Cancelled");
        }

        [Fact]
        public async Task Cancel_ManagerCancel_BlockedAfterNotified()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddDays(1));

            var act = () => _sut.CancelAsync(order.Id, _manager.Id, "SalesManager", new CancelPriceUpdateOrderRequest());

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Cancel_CeoCancel_AllowedAfterNotified()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddDays(1));

            var dto = await _sut.CancelAsync(order.Id, _ceo.Id, "CEO", new CancelPriceUpdateOrderRequest { Reason = "Thị trường biến động" });

            dto.Status.Should().Be("Cancelled");
        }

        [Fact]
        public async Task Cancel_AlreadyExecuted_Throws()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100_000m);
            var order = await ProposeAndAssign(product.Id, 98_000m, DateTime.UtcNow.AddHours(7).Date);
            await _sut.ExecuteAsync(order.Id, _staff.Id);

            var act = () => _sut.CancelAsync(order.Id, _ceo.Id, "CEO", new CancelPriceUpdateOrderRequest());

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
