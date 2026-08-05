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
    /// UpcomingDeliveryReminderJob: đơn DeliveryStatus=Scheduled với ScheduledDeliveryDate = ngày mai
    /// (giờ VN) -> nhắc nhân viên kho + Sale phụ trách, có cooldown 20h tránh nhắc trùng.
    /// </summary>
    public class UpcomingDeliveryReminderJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _notification = new();
        private readonly UpcomingDeliveryReminderJob _sut;
        private readonly User _warehouseStaff;
        private readonly (User user, CustomerProfile profile) _customer;
        private readonly User _salesStaff;

        public UpcomingDeliveryReminderJobTests()
        {
            _sut = new UpcomingDeliveryReminderJob(_db, _notification.Object, NullLogger<UpcomingDeliveryReminderJob>.Instance);

            _warehouseStaff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _salesStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.AddRange(_warehouseStaff, _salesStaff);
            _db.SaveChanges();

            _customer = TestData.SeedCustomer(_db);
            _customer.profile.AssignedSalesStaffId = _salesStaff.Id;
            _db.SaveChanges();
        }

        private Order SeedScheduledOrder(DateTime scheduledDate)
        {
            var order = TestData.Order(_customer.profile.Id, o =>
            {
                o.DeliveryStatus = DeliveryStatus.Scheduled;
                o.ScheduledDeliveryDate = scheduledDate;
                o.WarehouseStaffId = _warehouseStaff.Id;
            });
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        private static DateTime LocalTomorrow() => DateTime.UtcNow.AddHours(7).Date.AddDays(1);

        [Fact]
        public async Task OrderScheduledTomorrow_NotifiesWarehouseStaffAndSales()
        {
            var order = SeedScheduledOrder(LocalTomorrow());

            var reminded = await _sut.RunAsync(CancellationToken.None);

            reminded.Should().Be(1);
            _notification.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_29_UpcomingDeliveryReminder, _warehouseStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once());
            _notification.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_29_UpcomingDeliveryReminder, _salesStaff.Id,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once());
        }

        [Fact]
        public async Task OrderScheduledInThreeDays_NotReminded()
        {
            SeedScheduledOrder(LocalTomorrow().AddDays(2));

            var reminded = await _sut.RunAsync(CancellationToken.None);

            reminded.Should().Be(0);
            _notification.Verify(n => n.CreateNotificationAsync(
                It.IsAny<NotificationType>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>()),
                Times.Never());
        }

        [Fact]
        public async Task AlreadyRemindedRecently_NoDuplicateReminder()
        {
            var order = SeedScheduledOrder(LocalTomorrow());
            _db.Notifications.Add(new Notification
            {
                Type = NotificationType.SYS_29_UpcomingDeliveryReminder,
                RecipientUserId = _warehouseStaff.Id,
                Title = "Nhắc lịch giao hàng ngày mai",
                Body = "…",
                ReferenceId = order.Id,
                ReferenceType = "Order",
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            });
            _db.SaveChanges();

            var reminded = await _sut.RunAsync(CancellationToken.None);

            reminded.Should().Be(0);
        }
    }
}
