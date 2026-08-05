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
    /// Sheet: ScheduledJobs — L1-SJOB-11..13 (LowStockAlertJob).
    /// Ngưỡng nằm trên DÒNG Inventory (Inventory.ReorderThreshold), không phải SystemConfig.
    /// Cooldown: 24 giờ cho hàng thành phẩm, 2 ngày cho nguyên vật liệu.
    /// </summary>
    public class LowStockAlertJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _notification = new();
        private readonly LowStockAlertJob _sut;
        private readonly Guid _locationId;

        public LowStockAlertJobTests()
        {
            _sut = new LowStockAlertJob(_db, _notification.Object, NullLogger<LowStockAlertJob>.Instance);

            var (warehouse, location) = TestData.Warehouse();
            _db.Warehouses.Add(warehouse);
            _db.SaveChanges();
            _locationId = location.Id;
        }

        private Inventory SeedInventory(int available, int threshold)
        {
            var product = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(product.Id, _locationId, available, i => i.ReorderThreshold = threshold);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            return inv;
        }

        private void VerifyAlertCount(Guid referenceId, Times times) =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_20_LowStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), referenceId, "Inventory"), times);

        // L1-SJOB-11 | BVA | Tồn khả dụng tại threshold+1 / threshold / threshold-1 quanh ngưỡng 20
        //
        // 🔴 DEFECT CANDIDATE v2.2 (doc đã đánh dấu):
        //   SRS FT-12 AC-04 yêu cầu cảnh báo khi tồn "crossing to OR BELOW threshold" (<=),
        //   nhưng LowStockAlertJob.cs:44 dùng `if (inv.AvailableQuantity >= inv.ReorderThreshold) continue;`
        //   -> chỉ cảnh báo khi tồn NHỎ HƠN HẲN ngưỡng. Tại ĐÚNG ngưỡng 20 code KHÔNG cảnh báo.
        //   Test này assert THEO SPEC nên sẽ ĐỎ cho tới khi đổi `>=` thành `>`.
        //   Đối chiếu: Material.IsBelowSafetyThreshold() dùng `CurrentStock <= SafetyThreshold` (đúng spec)
        //   -> hai nhánh của cùng 1 job đang không nhất quán.
        [Theory]
        [InlineData(21, false)] // trên ngưỡng -> không cảnh báo
        [InlineData(20, true)]  // ĐÚNG ngưỡng -> SPEC yêu cầu CÓ cảnh báo (code hiện tại: không)
        [InlineData(19, true)]  // dưới ngưỡng -> có cảnh báo
        public async Task L1_SJOB_11_AlertsAtOrBelowReorderThreshold(int available, bool expectAlert)
        {
            var inv = SeedInventory(available, threshold: 20);

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlertCount(inv.Id, expectAlert ? Times.Once() : Times.Never());
        }

        // L1-SJOB-12 | EP-Valid | Không tạo cảnh báo trùng khi đang trong thời gian cooldown
        [Fact]
        public async Task L1_SJOB_12_Inventory_WithinCooldown_NoDuplicateAlert()
        {
            var inv = SeedInventory(available: 5, threshold: 20);
            _db.Notifications.Add(new Notification
            {
                Type = NotificationType.SYS_20_LowStockAlert,
                RecipientUserId = Guid.NewGuid(),
                Title = "Tồn kho thấp",
                Body = "…",
                ReferenceId = inv.Id,
                ReferenceType = "Inventory",
                CreatedAt = DateTime.UtcNow.AddHours(-1) // trong cooldown 24h
            });
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlertCount(inv.Id, Times.Never());
        }

        // L1-SJOB-12 (nhánh nguyên vật liệu) | EP-Valid | Cooldown 2 ngày cho Material
        [Theory]
        [InlineData(1, false)] // cảnh báo cách đây 1 ngày -> còn trong cooldown 2 ngày
        [InlineData(3, true)]  // cách đây 3 ngày -> đã hết cooldown
        public async Task L1_SJOB_12_Material_TwoDayCooldown(int daysSinceLastAlert, bool expectAlert)
        {
            var material = new Material
            {
                Name = "Nhựa PP",
                Unit = "Cây",
                CurrentStock = 5,
                SafetyThreshold = 20,
                LastAlertSentDate = DateTime.UtcNow.AddDays(-daysSinceLastAlert)
            };
            _db.Materials.Add(material);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            _notification.Verify(n => n.CreateRoleNotificationAsync(
                    NotificationType.SYS_20_LowStockAlert, SystemRole.WarehouseStaff,
                    It.IsAny<string>(), It.IsAny<string>(), material.Id, "Material"),
                expectAlert ? Times.Once() : Times.Never());
        }

        // L1-SJOB-13 | EP-Valid | Tồn hồi phục TRÊN ngưỡng -> lượt chạy sau không cảnh báo nữa
        [Fact]
        public async Task L1_SJOB_13_StockRecovered_StopsAlerting()
        {
            var inv = SeedInventory(available: 5, threshold: 20);
            await _sut.RunAsync(CancellationToken.None);
            VerifyAlertCount(inv.Id, Times.Once());

            // Nhập hàng đưa tồn lên trên ngưỡng
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity = 50;
            _db.SaveChanges();
            _notification.Invocations.Clear();

            await _sut.RunAsync(CancellationToken.None);

            VerifyAlertCount(inv.Id, Times.Never(), "tồn đã hồi phục, cảnh báo phải dừng");
        }

        private void VerifyAlertCount(Guid referenceId, Times times, string because) =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_20_LowStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), referenceId, "Inventory"), times, because);
    }
}
