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
        private readonly Mock<ISystemConfigService> _sysConfig = new();
        private readonly SlowMovingStockAlertJob _sut;

        public SlowMovingStockAlertJobTests()
        {
            _inventoryService = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object, new Mock<ILogger<InventoryService>>().Object, TestWarehouseAccessGuard.Create(_db));
            _sut = new SlowMovingStockAlertJob(_db, _inventoryService, _notification.Object, _sysConfig.Object, NullLogger<SlowMovingStockAlertJob>.Instance);
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

        private Inventory SeedInventoryWithLastOutbound(int daysAgo, int onHand = 10)
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, onHand);
            _db.Inventories.Add(inv);
            var user = TestData.User();
            _db.Users.Add(user);
            _db.StockTransactions.Add(new StockTransaction
            {
                InventoryId = inv.Id,
                ProductId = product.Id,
                WarehouseLocationId = loc.Id,
                QuantityChange = -1,
                TransactionType = TransactionType.GoodsIssue,
                CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            });
            _db.SaveChanges();
            return inv;
        }

        private void VerifyAlert(Guid inventoryId, Times times, string because = "") =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_51_SlowMovingStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), inventoryId, It.IsAny<string>()), times, because);

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

        // BUGFIX: ngưỡng ngày trước đây hardcode 30, không cấu hình được — nay CEO chỉnh qua
        // SLOW_MOVING_DAYS_THRESHOLD (api/ceo/system-configs). Cùng 1 item xuất kho gần đây (10 ngày
        // trước) phải KHÔNG bị coi là chậm luân chuyển với ngưỡng mặc định 30 ngày, nhưng PHẢI bị
        // coi là chậm luân chuyển khi CEO hạ ngưỡng xuống 5 ngày.
        [Fact]
        public async Task ConfiguredThreshold_OverridesDefault30Days()
        {
            var inv = SeedInventoryWithLastOutbound(daysAgo: 10);

            await _sut.RunAsync(CancellationToken.None);
            VerifyAlert(inv.Id, Times.Never(), "10 ngày trước vẫn trong hạn mặc định 30 ngày -> chưa phải chậm luân chuyển");

            _sysConfig.Setup(s => s.GetEffectiveValueAsync("SLOW_MOVING_DAYS_THRESHOLD", It.IsAny<DateTime?>()))
                .ReturnsAsync("5");

            await _sut.RunAsync(CancellationToken.None);
            VerifyAlert(inv.Id, Times.Once(), "hạ ngưỡng xuống 5 ngày -> 10 ngày trước đã vượt ngưỡng, phải cảnh báo");
        }
    }
}
