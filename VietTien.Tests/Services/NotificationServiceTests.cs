using FluentAssertions;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: NotificationService — L1-NOTI-01..03. EF InMemory + mock IHubContext&lt;NotificationHub&gt;.</summary>
    public class NotificationServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly NotificationService _sut;

        public NotificationServiceTests()
        {
            _sut = new NotificationService(_db, MockHubContext.Create<NotificationHub>().Object);
        }

        // L1-NOTI-01 | EP-Valid | Tạo notification lưu đủ type, người nhận, title/body, reference
        [Fact]
        public async Task L1_NOTI_01_CreateNotification_PersistsAllFields()
        {
            var u1 = TestData.User();
            _db.Users.Add(u1);
            _db.SaveChanges();
            var refId = Guid.NewGuid();

            await _sut.CreateNotificationAsync(NotificationType.SYS_02_NewOrder, u1.Id, "Đơn mới", "Nội dung", refId, "Order");

            var n = _db.Notifications.Single();
            n.RecipientUserId.Should().Be(u1.Id);
            n.Type.Should().Be(NotificationType.SYS_02_NewOrder);
            n.Title.Should().Be("Đơn mới");
            n.Body.Should().Be("Nội dung");
            n.ReferenceId.Should().Be(refId);
            n.ReferenceType.Should().Be("Order");
            n.IsRead.Should().BeFalse(); // chưa đọc
        }

        // L1-NOTI-02 | EP-Valid | Role notification -> đúng 1 bản ghi cho MỖI user giữ role, role khác không nhận
        [Fact]
        public async Task L1_NOTI_02_RoleNotification_OneRowPerSalesStaff()
        {
            _db.Users.AddRange(
                TestData.User(u => u.Role = SystemRole.SalesStaff),
                TestData.User(u => u.Role = SystemRole.SalesStaff),
                TestData.User(u => u.Role = SystemRole.SalesStaff),
                TestData.User(u => u.Role = SystemRole.Customer),
                TestData.User(u => u.Role = SystemRole.WarehouseStaff));
            _db.SaveChanges();

            await _sut.CreateRoleNotificationAsync(NotificationType.SYS_02_NewOrder, SystemRole.SalesStaff, "Đơn mới", "Body");

            _db.Notifications.Count().Should().Be(3);
            var salesIds = _db.Users.Where(u => u.Role == SystemRole.SalesStaff).Select(u => u.Id).ToList();
            _db.Notifications.Select(n => n.RecipientUserId).ToList().Should().BeEquivalentTo(salesIds);
        }

        // L1-NOTI-03 | EP-Invalid | RecipientUserId không tồn tại -> hành vi thật: vẫn insert bản ghi (không FK check ở InMemory),
        // không crash. Ghi Notes: code không validate người nhận tồn tại.
        [Fact]
        public async Task L1_NOTI_03_CreateNotification_UnknownRecipient_NoCrash()
        {
            var ghostId = Guid.NewGuid(); // không có user nào

            var act = () => _sut.CreateNotificationAsync(NotificationType.SYS_02_NewOrder, ghostId, "T", "B");

            await act.Should().NotThrowAsync(); // không exception bắn ra caller
        }
    }
}
