using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// Sheet: ScheduledJobs — L1-SJOB-01..04 (SePayReservationExpiryJob).
    /// Job đọc ngưỡng qua SystemConfigService.GetEffectiveValueAsync("SEPAY_RESERVATION_MINUTES"),
    /// nên test SEED CONFIG chứ không hard-code 15. Không có IClock trong codebase — điều khiển
    /// thời gian bằng cách BACK-DATE Order.CreatedAt.
    /// </summary>
    public class SePayReservationExpiryJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly FakeInventoryReservationService _reservation;
        private readonly SePayReservationExpiryJob _sut;
        private readonly Guid _locationId;

        public SePayReservationExpiryJobTests()
        {
            _reservation = new FakeInventoryReservationService(_db);
            var configService = new SystemConfigService(_db, new NoOpAuditLogService());
            _sut = new SePayReservationExpiryJob(_db, configService, _reservation, NullLogger<SePayReservationExpiryJob>.Instance);

            var (warehouse, location) = TestData.Warehouse();
            _db.Warehouses.Add(warehouse);
            _db.SaveChanges();
            _locationId = location.Id;

            TestData.SeedConfig(_db, "SEPAY_RESERVATION_MINUTES", "15");
        }

        /// <summary>Đơn SePay Draft/Pending đã giữ mềm 3 đơn vị P1, tạo cách đây minutesAgo phút.</summary>
        private (Order order, Product product) SeedExpiringOrder(double minutesAgo, int onHand = 10, int reserved = 3)
        {
            var product = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(product.Id, _locationId, onHand, inv => inv.ReservedQuantity = reserved));

            var (_, profile) = TestData.SeedCustomer(_db);
            var order = TestData.Order(profile.Id, o =>
            {
                o.OrderStatus = OrderStatus.Draft;
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = PaymentStatus.Pending;
                o.CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
            });
            _db.Orders.Add(order);
            _db.SaveChanges();

            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, reserved));
            _db.SaveChanges();
            return (order, product);
        }

        private Inventory Inv(Guid productId)
        {
            _db.ChangeTracker.Clear();
            return _db.Inventories.Single(i => i.ProductId == productId);
        }

        // L1-SJOB-01 | BVA-Max | Reservation mới 14 phút -> CHƯA hết hạn, không bị giải phóng
        [Fact]
        public async Task L1_SJOB_01_At14Minutes_ReservationSurvives()
        {
            var (order, product) = SeedExpiringOrder(minutesAgo: 14);

            await _sut.RunAsync(CancellationToken.None);

            Inv(product.Id).ReservedQuantity.Should().Be(3, "chưa tới ngưỡng 15 phút");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Draft);
        }

        // L1-SJOB-02 | BVA-Max+1 | Đúng/quá 15 phút -> giải phóng Reserved, đơn chuyển Cancelled
        [Theory]
        [InlineData(15)]
        [InlineData(16)]
        public async Task L1_SJOB_02_AtOrPast15Minutes_ReleasesReservation(double minutesAgo)
        {
            var (order, product) = SeedExpiringOrder(minutesAgo);

            await _sut.RunAsync(CancellationToken.None);

            var inv = Inv(product.Id);
            inv.ReservedQuantity.Should().Be(0);
            inv.AvailableQuantity.Should().Be(10, "tồn quay lại khả dụng");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Cancelled);
        }

        // L1-SJOB-03 | EP-Valid | Job CHỈ giải phóng phần Reserved, KHÔNG đụng tới Allocated
        [Fact]
        public async Task L1_SJOB_03_DoesNotTouchAllocatedQuantity()
        {
            // Đơn B: quá hạn, đang giữ mềm 2 -> phải được giải phóng
            var (_, productB) = SeedExpiringOrder(minutesAgo: 20, onHand: 10, reserved: 2);

            // Đơn A: đã Allocated 3 (đã xác nhận) -> job không được chạm vào
            var productA = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(productA.Id, _locationId, 10, inv => inv.AllocatedQuantity = 3));
            var (_, profileA) = TestData.SeedCustomer(_db);
            var orderA = TestData.Order(profileA.Id, o =>
            {
                o.OrderStatus = OrderStatus.Confirmed;   // đã xác nhận -> không thuộc phạm vi job
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = PaymentStatus.Pending;
                o.CreatedAt = DateTime.UtcNow.AddMinutes(-60);
            });
            _db.Orders.Add(orderA);
            _db.SaveChanges();
            _db.OrderItems.Add(TestData.OrderItem(orderA.Id, productA.Id, 3));
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            Inv(productB.Id).ReservedQuantity.Should().Be(0, "phần giữ mềm quá hạn được trả lại");
            Inv(productA.Id).AllocatedQuantity.Should().Be(3, "job KHÔNG gọi ReleaseAllocatedAsync");
        }

        // L1-SJOB-04 | Idempotency | Chạy job 2 lần liên tiếp -> không hoàn tồn 2 lần
        [Fact]
        public async Task L1_SJOB_04_RunTwice_DoesNotReleaseTwice()
        {
            var (_, product) = SeedExpiringOrder(minutesAgo: 20, onHand: 10, reserved: 3);

            await _sut.RunAsync(CancellationToken.None);
            var afterFirst = Inv(product.Id).AvailableQuantity;

            await _sut.RunAsync(CancellationToken.None);
            var afterSecond = Inv(product.Id).AvailableQuantity;

            afterFirst.Should().Be(10);
            afterSecond.Should().Be(10, "đơn đã Cancelled nên không nằm trong phạm vi lượt chạy thứ 2");
        }
    }
}
