using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// Sheet: ScheduledJobs — L1-SJOB-05..08 (OrderSlaJob).
    ///
    /// ⚠ KHÁC DOC: OrderSlaJob HARD-CODE các mốc 25 / 30 / 35 phút, KHÔNG đọc SystemConfig
    ///   ('COD_WARNING_MINUTES' / 'COD_ESCALATION_MINUTES' / 'COD_RESERVATION_MINUTES').
    ///   Giá trị seed mặc định trùng với mốc hard-code nên hành vi vẫn đúng SRS; test dưới đây bám
    ///   theo giá trị 25/30/35. Đây là CÙNG LOẠI khiếm khuyết với L1-ORD-73 nhưng doc chưa đánh dấu.
    ///   Xem DOC_MISMATCHES.md.
    ///
    /// Không có IClock — điều khiển thời gian bằng cách back-date Order.CreatedAt.
    /// </summary>
    public class OrderSlaJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _notification = new();
        private readonly FakeInventoryReservationService _reservation;
        private readonly OrderSlaJob _sut;
        private readonly Guid _locationId;
        private readonly User _sales;

        public OrderSlaJobTests()
        {
            _reservation = new FakeInventoryReservationService(_db);
            _sut = new OrderSlaJob(_db, _notification.Object, _reservation, NullLogger<OrderSlaJob>.Instance);

            var (warehouse, location) = TestData.Warehouse();
            _db.Warehouses.Add(warehouse);
            _sales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(_sales);
            _db.SaveChanges();
            _locationId = location.Id;
        }

        /// <summary>Đơn COD chờ Sales xác nhận, tạo cách đây minutesAgo phút, đang giữ mềm 3 đơn vị.</summary>
        private (Order order, Product product) SeedPendingCodOrder(double minutesAgo)
        {
            var product = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(product.Id, _locationId, 10, inv => inv.ReservedQuantity = 3));

            var (_, profile) = TestData.SeedCustomer(_db);
            profile.AssignedSalesStaffId = _sales.Id;

            var order = TestData.Order(profile.Id, o =>
            {
                o.OrderStatus = OrderStatus.PendingConfirmation;
                o.PaymentMethod = PaymentMethod.COD;
                o.CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
            });
            _db.Orders.Add(order);
            _db.SaveChanges();

            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, 3));
            _db.SaveChanges();
            return (order, product);
        }

        // L1-SJOB-05 | BVA | Mốc cảnh báo Sales: 24 phút chưa cảnh báo; 25 và 26 phút thì có đúng 1 cảnh báo
        [Theory]
        [InlineData(24, false)]
        [InlineData(25, true)]
        [InlineData(26, true)]
        public async Task L1_SJOB_05_WarnsAssignedSalesAt25Minutes(double minutesAgo, bool expectWarning)
        {
            SeedPendingCodOrder(minutesAgo);

            await _sut.RunAsync(CancellationToken.None);

            _notification.Verify(n => n.CreateNotificationAsync(
                    NotificationType.SYS_03_CodUnconfirmed25m, _sales.Id,
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()),
                expectWarning ? Times.Once() : Times.Never());
        }

        // L1-SJOB-06 | BVA | Mốc escalate Sales Manager: 29 phút chưa escalate; 30 và 31 phút thì có
        [Theory]
        [InlineData(29, false)]
        [InlineData(30, true)]
        [InlineData(31, true)]
        public async Task L1_SJOB_06_EscalatesToManagerAt30Minutes(double minutesAgo, bool expectEscalation)
        {
            SeedPendingCodOrder(minutesAgo);

            await _sut.RunAsync(CancellationToken.None);

            _notification.Verify(n => n.CreateRoleNotificationAsync(
                    NotificationType.SYS_04_CodUnconfirmed30m, SystemRole.SalesManager,
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()),
                expectEscalation ? Times.Once() : Times.Never());
        }

        // L1-SJOB-07 | BVA-Max+1 | Mốc hết hạn giữ hàng: 34 phút vẫn giữ; 35 và 36 phút thì trả tồn + huỷ đơn
        [Theory]
        [InlineData(34, false)]
        [InlineData(35, true)]
        [InlineData(36, true)]
        public async Task L1_SJOB_07_ReleasesReservationAt35Minutes(double minutesAgo, bool expectRelease)
        {
            var (order, product) = SeedPendingCodOrder(minutesAgo);

            await _sut.RunAsync(CancellationToken.None);

            _db.ChangeTracker.Clear();
            var inv = _db.Inventories.Single(i => i.ProductId == product.Id);
            var saved = _db.Orders.Single(o => o.Id == order.Id);

            if (expectRelease)
            {
                inv.ReservedQuantity.Should().Be(0);
                inv.AvailableQuantity.Should().Be(10);
                saved.OrderStatus.Should().Be(OrderStatus.Cancelled);
            }
            else
            {
                inv.ReservedQuantity.Should().Be(3);
                saved.OrderStatus.Should().Be(OrderStatus.PendingConfirmation);
            }
        }

        // L1-SJOB-08 | Idempotency | Chạy lại job -> không tạo cảnh báo TRÙNG cho cùng 1 đơn
        [Fact]
        public async Task L1_SJOB_08_RunTwice_DoesNotDuplicateWarning()
        {
            var (order, _) = SeedPendingCodOrder(minutesAgo: 26);
            await _sut.RunAsync(CancellationToken.None);

            // Job kiểm tra trùng bằng bảng Notifications — mô phỏng notification đã được ghi ở lượt trước.
            _db.Notifications.Add(new Notification
            {
                Type = NotificationType.SYS_03_CodUnconfirmed25m,
                RecipientUserId = _sales.Id,
                Title = "Đơn hàng COD sắp quá hạn",
                Body = "…",
                ReferenceId = order.Id,
                ReferenceType = "Order"
            });
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            _notification.Verify(n => n.CreateNotificationAsync(
                    NotificationType.SYS_03_CodUnconfirmed25m, _sales.Id,
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()),
                Times.Once, "không được cảnh báo lần thứ 2 cho cùng nghiệp vụ");
        }
    }
}
