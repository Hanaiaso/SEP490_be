using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VietTien.API.Data;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// SlowMovingStockAlertJob — cảnh báo chủ động cho mặt hàng chậm luân chuyển, tái dùng đúng query
    /// đã có ở InventoryService.GetSlowMovingItemsAsync (không viết lại logic tính), chỉ thêm gửi
    /// notification + cooldown per-row 2 ngày (mirror Material.LastAlertSentDate).
    /// </summary>
    public class SlowMovingStockAlertJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly InventoryService _inventoryService;
        private readonly Mock<INotificationService> _notification = new();
        private readonly SlowMovingStockAlertJob _sut;

        public SlowMovingStockAlertJobTests()
        {
            _inventoryService = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object, new Mock<ILogger<InventoryService>>().Object, TestWarehouseAccessGuard.Create(_db));
            _sut = new SlowMovingStockAlertJob(_db, _inventoryService, _notification.Object, NullLogger<SlowMovingStockAlertJob>.Instance);
        }

        private Inventory SeedNeverIssuedInventory(int onHand = 10)
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, onHand);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            return inv;
        }

        private void VerifyAlert(Guid inventoryId, Times times) =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_51_SlowMovingStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), inventoryId, It.IsAny<string>()), times);

        [Fact]
        public async Task NeverIssuedItem_WithStockOnHand_GetsAlerted()
        {
            var inv = SeedNeverIssuedInventory();

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlert(inv.Id, Times.Once());
        }

        [Fact]
        public async Task ZeroOnHand_NotAlerted()
        {
            var inv = SeedNeverIssuedInventory(onHand: 0);

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlert(inv.Id, Times.Never());
        }

        [Fact]
        public async Task WithinCooldown_NoDuplicateAlert()
        {
            var inv = SeedNeverIssuedInventory();
            await _sut.RunAsync(CancellationToken.None);
            VerifyAlert(inv.Id, Times.Once());
            _notification.Invocations.Clear();

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlert(inv.Id, Times.Never());
        }

        [Fact]
        public async Task AfterCooldownExpires_AlertsAgain()
        {
            var inv = SeedNeverIssuedInventory();
            await _sut.RunAsync(CancellationToken.None);
            _notification.Invocations.Clear();

            _db.Inventories.Single(i => i.Id == inv.Id).LastSlowMovingAlertSentAt = DateTime.UtcNow.AddDays(-3);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlert(inv.Id, Times.Once());
        }
    }
}
