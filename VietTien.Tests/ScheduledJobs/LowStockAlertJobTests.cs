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
    /// Ngưỡng tồn thấp/tồn đọng nay nằm trên SẢN PHẨM/NGUYÊN VẬT LIỆU (Product.ReorderThreshold/
    /// ExcessThreshold, Material.SafetyThreshold/MaxStockThreshold), tính GỘP theo tổng khả dụng mọi
    /// kho — không còn theo từng dòng Inventory riêng lẻ. Cooldown: 24 giờ cho Product (LastAlertSentDate),
    /// 2 ngày cho Material (LastAlertSentDate) — dùng CHUNG cho cả 2 loại cảnh báo (thấp/đọng) vì 1 mặt
    /// hàng không thể vừa thấp vừa đọng cùng lúc.
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

        private Product SeedProductWithStock(int available, int? reorderThreshold = null, int? excessThreshold = null, DateTime? lastAlertSentDate = null)
        {
            var product = TestData.SeedProduct(_db, p =>
            {
                p.ReorderThreshold = reorderThreshold;
                p.ExcessThreshold = excessThreshold;
                p.LastAlertSentDate = lastAlertSentDate;
            });
            var inv = TestData.Inventory(product.Id, _locationId, available);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            return product;
        }

        private void VerifyLowStockAlert(Guid referenceId, Times times) =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_20_LowStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), referenceId, "Product"), times);

        private void VerifyExcessStockAlert(Guid referenceId, Times times) =>
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_50_ExcessStockAlert, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), referenceId, It.IsAny<string>()), times);

        // L1-SJOB-11 | BVA | Tồn khả dụng tại threshold+1 / threshold / threshold-1 quanh ngưỡng 20
        [Theory]
        [InlineData(21, false)] // trên ngưỡng -> không cảnh báo
        [InlineData(20, true)]  // ĐÚNG ngưỡng -> có cảnh báo (crossing to or below)
        [InlineData(19, true)]  // dưới ngưỡng -> có cảnh báo
        public async Task L1_SJOB_11_AlertsAtOrBelowReorderThreshold(int available, bool expectAlert)
        {
            var product = SeedProductWithStock(available, reorderThreshold: 20);

            await _sut.RunAsync(CancellationToken.None);

            VerifyLowStockAlert(product.Id, expectAlert ? Times.Once() : Times.Never());
        }

        // Tồn thấp Product nay GỘP theo tổng khả dụng nhiều kho — tổng dưới ngưỡng dù từng dòng riêng lẻ
        // vẫn còn hàng phải được cảnh báo.
        [Fact]
        public async Task Product_LowStock_AggregatesAcrossMultipleWarehouses()
        {
            var product = TestData.SeedProduct(_db, p => p.ReorderThreshold = 20);
            var (warehouse2, location2) = TestData.Warehouse();
            _db.Warehouses.Add(warehouse2);
            _db.SaveChanges();

            _db.Inventories.Add(TestData.Inventory(product.Id, _locationId, 8));
            _db.Inventories.Add(TestData.Inventory(product.Id, location2.Id, 7));
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            VerifyLowStockAlert(product.Id, Times.Once());
        }

        // L1-SJOB-12 | EP-Valid | Không tạo cảnh báo trùng khi đang trong thời gian cooldown (24h, Product)
        [Theory]
        [InlineData(1, false)]  // cảnh báo cách đây 1 giờ -> còn trong cooldown 24h
        [InlineData(25, true)]  // cách đây 25 giờ -> đã hết cooldown
        public async Task L1_SJOB_12_Product_TwentyFourHourCooldown(int hoursSinceLastAlert, bool expectAlert)
        {
            var product = SeedProductWithStock(available: 5, reorderThreshold: 20,
                lastAlertSentDate: DateTime.UtcNow.AddHours(-hoursSinceLastAlert));

            await _sut.RunAsync(CancellationToken.None);

            VerifyLowStockAlert(product.Id, expectAlert ? Times.Once() : Times.Never());
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
            var product = SeedProductWithStock(available: 5, reorderThreshold: 20);
            await _sut.RunAsync(CancellationToken.None);
            VerifyLowStockAlert(product.Id, Times.Once());
            _notification.Invocations.Clear();

            // Nhập hàng đưa tồn lên trên ngưỡng + đẩy LastAlertSentDate ra ngoài cooldown để cô lập
            // đúng nguyên nhân "tồn đã hồi phục" khỏi việc chỉ đơn thuần còn trong cooldown.
            var inv = _db.Inventories.Single(i => i.ProductId == product.Id);
            inv.OnHandQuantity = 50;
            _db.Products.Single(p => p.Id == product.Id).LastAlertSentDate = DateTime.UtcNow.AddHours(-25);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            VerifyLowStockAlert(product.Id, Times.Never());
        }

        // Tồn đọng Product: vượt ngưỡng tối đa -> SYS_50, đúng ngưỡng hoặc dưới ngưỡng -> không cảnh báo.
        [Theory]
        [InlineData(100, false)] // đúng ngưỡng -> không tồn đọng
        [InlineData(101, true)]  // vượt ngưỡng -> tồn đọng
        public async Task Product_ExcessStock_AlertsAboveMaxThreshold(int available, bool expectAlert)
        {
            var product = SeedProductWithStock(available, excessThreshold: 100);

            await _sut.RunAsync(CancellationToken.None);

            VerifyExcessStockAlert(product.Id, expectAlert ? Times.Once() : Times.Never());
        }

        // Nguyên vật liệu tồn đọng: vượt MaxStockThreshold -> SYS_50.
        [Fact]
        public async Task Material_ExcessStock_AlertsAboveMaxThreshold()
        {
            var material = new Material
            {
                Name = "Lõi giấy",
                Unit = "Cuộn",
                CurrentStock = 500,
                SafetyThreshold = 20,
                MaxStockThreshold = 300
            };
            _db.Materials.Add(material);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            VerifyExcessStockAlert(material.Id, Times.Once());
        }

        // 1 sản phẩm không thể vừa "thấp" vừa "đọng" cùng lúc — nếu cấu hình cả 2 ngưỡng nhưng tồn hiện
        // tại đang thấp thì chỉ gửi SYS_20, không gửi thêm SYS_50 trong cùng lượt chạy.
        [Fact]
        public async Task Product_WhenLow_DoesNotAlsoAlertExcess()
        {
            var product = SeedProductWithStock(available: 5, reorderThreshold: 20, excessThreshold: 100);

            await _sut.RunAsync(CancellationToken.None);

            VerifyLowStockAlert(product.Id, Times.Once());
            VerifyExcessStockAlert(product.Id, Times.Never());
        }
    }
}
