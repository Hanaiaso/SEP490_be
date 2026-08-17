using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// GetLowStockAlertsAsync/GetExcessStockAlertsAsync — nguồn xác thực duy nhất cho trang Cảnh báo
    /// tồn kho (WarehouseLowStock.tsx) và gợi ý bổ sung trong modal tạo PO của CEO. Product tính GỘP
    /// theo tổng khả dụng mọi kho (mirror Material), không còn theo từng dòng Inventory riêng lẻ.
    /// </summary>
    public class InventoryStockAlertsTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly InventoryService _sut;

        public InventoryStockAlertsTests()
        {
            _sut = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object, new Mock<ILogger<InventoryService>>().Object, TestWarehouseAccessGuard.Create(_db));
        }

        [Fact]
        public async Task GetLowStockAlertsAsync_AggregatesProductAcrossWarehouses()
        {
            var product = TestData.SeedProduct(_db, p => p.ReorderThreshold = 20);
            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.Inventories.AddRange(
                TestData.Inventory(product.Id, loc1.Id, 8),
                TestData.Inventory(product.Id, loc2.Id, 7));
            _db.SaveChanges();

            var alerts = await _sut.GetLowStockAlertsAsync();

            var alert = alerts.Should().ContainSingle(a => a.ItemId == product.Id).Subject;
            alert.ItemType.Should().Be("Product");
            alert.AvailableQuantity.Should().Be(15);
            alert.Threshold.Should().Be(20);
        }

        [Fact]
        public async Task GetLowStockAlertsAsync_ProductAboveThreshold_NotIncluded()
        {
            var product = TestData.SeedProduct(_db, p => p.ReorderThreshold = 20);
            _db.Inventories.Add(TestData.Inventory(product.Id, SeedLocation(), 21));
            _db.SaveChanges();

            var alerts = await _sut.GetLowStockAlertsAsync();

            alerts.Should().NotContain(a => a.ItemId == product.Id);
        }

        [Fact]
        public async Task GetLowStockAlertsAsync_IncludesMaterialBelowSafetyThreshold()
        {
            var material = new Material { Name = "Nhựa PP", Unit = "Cây", CurrentStock = 5, SafetyThreshold = 20 };
            _db.Materials.Add(material);
            _db.SaveChanges();

            var alerts = await _sut.GetLowStockAlertsAsync();

            var alert = alerts.Should().ContainSingle(a => a.ItemId == material.Id).Subject;
            alert.ItemType.Should().Be("Material");
            alert.AvailableQuantity.Should().Be(5);
        }

        [Fact]
        public async Task GetExcessStockAlertsAsync_AggregatesProductAcrossWarehouses()
        {
            var product = TestData.SeedProduct(_db, p => p.ExcessThreshold = 100);
            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.Inventories.AddRange(
                TestData.Inventory(product.Id, loc1.Id, 60),
                TestData.Inventory(product.Id, loc2.Id, 50));
            _db.SaveChanges();

            var alerts = await _sut.GetExcessStockAlertsAsync();

            var alert = alerts.Should().ContainSingle(a => a.ItemId == product.Id).Subject;
            alert.AvailableQuantity.Should().Be(110);
            alert.Threshold.Should().Be(100);
        }

        [Fact]
        public async Task GetExcessStockAlertsAsync_ProductAtOrBelowThreshold_NotIncluded()
        {
            var product = TestData.SeedProduct(_db, p => p.ExcessThreshold = 100);
            _db.Inventories.Add(TestData.Inventory(product.Id, SeedLocation(), 100));
            _db.SaveChanges();

            var alerts = await _sut.GetExcessStockAlertsAsync();

            alerts.Should().NotContain(a => a.ItemId == product.Id);
        }

        [Fact]
        public async Task GetExcessStockAlertsAsync_IncludesMaterialAboveMaxStockThreshold()
        {
            var material = new Material { Name = "Lõi giấy", Unit = "Cuộn", CurrentStock = 500, SafetyThreshold = 20, MaxStockThreshold = 300 };
            _db.Materials.Add(material);
            _db.SaveChanges();

            var alerts = await _sut.GetExcessStockAlertsAsync();

            var alert = alerts.Should().ContainSingle(a => a.ItemId == material.Id).Subject;
            alert.ItemType.Should().Be("Material");
            alert.AvailableQuantity.Should().Be(500);
        }

        private System.Guid SeedLocation()
        {
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.SaveChanges();
            return loc.Id;
        }
    }
}
