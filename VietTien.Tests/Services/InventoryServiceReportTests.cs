using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: InventoryService — bổ sung cho 3 hàm báo cáo/kiểm kê trước đó **chưa có một test nào**
    /// (`InventoryServiceTests` cũ chỉ phủ GetInventoryByWarehouse / Adjust / AddProductToWarehouse).
    ///
    /// Đây là lớp có branch coverage tệ nhất dự án (17,7%) vì 3 hàm này dày đặc ngã rẽ: filter tuỳ
    /// chọn, phân loại Product/Material, các mốc ngày. Nguyên tắc viết ở đây là **mỗi filter tuỳ chọn
    /// có 2 case (truyền / không truyền), mỗi ternary phân loại có đủ case bằng số vế** — chạm nhánh
    /// chứ không chỉ chạm dòng.
    /// </summary>
    public class InventoryServiceReportTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly InventoryService _sut;

        public InventoryServiceReportTests()
            => _sut = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object,
                new Mock<ILogger<InventoryService>>().Object, TestWarehouseAccessGuard.Create(_db));

        private Material SeedMaterial(string name = "Màng PE", double safety = 100)
        {
            var m = new Material { Name = name, Unit = "Cuộn", SafetyThreshold = safety };
            _db.Materials.Add(m);
            _db.SaveChanges();
            return m;
        }

        private StockTransaction Tx(Guid inventoryId, Guid locationId, int change,
            TransactionType type, DateTime at) => new()
        {
            InventoryId = inventoryId,
            WarehouseLocationId = locationId,
            QuantityChange = change,
            TransactionType = type,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = at
        };

        // ═══════════════════════════════════════════════════════════════════
        // GetInventoryReportAsync
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Report_WhenFromDateAfterToDate_Rejected()
        {
            var act = () => _sut.GetInventoryReportAsync(null, new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*không hợp lệ*");
        }

        [Fact]
        public async Task Report_WithoutDates_DefaultsToLast30Days()
        {
            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.StockMovement.Should().HaveCount(31,
                "mặc định 30 ngày gần nhất, tính cả hai đầu mút nên là 31 điểm");
        }

        [Fact]
        public async Task Report_CountsProductAndMaterialSkusSeparately()
        {
            var product = TestData.SeedProduct(_db);
            var material = SeedMaterial();
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.AddRange(
                TestData.Inventory(product.Id, loc.Id, 10),
                new Inventory { MaterialId = material.Id, WarehouseLocationId = loc.Id, OnHandQuantity = 5 });
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.TotalSkus.Should().Be(2, "1 sản phẩm + 1 nguyên vật liệu");
        }

        [Fact]
        public async Task Report_ValuesOnlyProductsNotMaterials()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 1_000m);
            var material = SeedMaterial();
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.AddRange(
                TestData.Inventory(product.Id, loc.Id, 10),
                new Inventory { MaterialId = material.Id, WarehouseLocationId = loc.Id, OnHandQuantity = 999 });
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.TotalInventoryValue.Should().Be(10_000m,
                "nguyên vật liệu không có đơn giá niêm yết nên không tính vào giá trị tồn");
        }

        [Fact]
        public async Task Report_WithWarehouseFilter_ExcludesOtherWarehousesAndReportsOneWarehouse()
        {
            var p1 = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100m);
            var p2 = TestData.SeedProduct(_db, p => p.StandardListedPrice = 100m);
            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.Inventories.AddRange(
                TestData.Inventory(p1.Id, loc1.Id, 3),
                TestData.Inventory(p2.Id, loc2.Id, 7));
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(w1.Id, null, null);

            report.TotalSkus.Should().Be(1);
            report.TotalInventoryValue.Should().Be(300m);
            report.TotalWarehouses.Should().Be(1, "lọc theo 1 kho thì không đếm toàn hệ thống");
        }

        [Fact]
        public async Task Report_WithoutWarehouseFilter_CountsAllWarehouses()
        {
            var (w1, _) = TestData.Warehouse();
            var (w2, _) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.TotalWarehouses.Should().Be(2);
        }

        [Fact]
        public async Task Report_FlagsLowStock_ForProductBelowReorderThresholdAndMaterialAtSafetyThreshold()
        {
            var product = TestData.SeedProduct(_db);
            var material = SeedMaterial(safety: 10);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.AddRange(
                // sản phẩm: dùng dấu < nên bằng ngưỡng thì KHÔNG tính là thấp
                TestData.Inventory(product.Id, loc.Id, 3, i => i.ReorderThreshold = 5),
                // nguyên vật liệu: dùng dấu <= nên bằng ngưỡng LÀ thấp
                new Inventory { MaterialId = material.Id, WarehouseLocationId = loc.Id, OnHandQuantity = 10 });
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.LowStockCount.Should().Be(2);
            report.TopLowStockItems.Should().HaveCount(2);
        }

        [Fact]
        public async Task Report_IgnoresProductWithoutReorderThreshold()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.Add(TestData.Inventory(product.Id, loc.Id, 0, i => i.ReorderThreshold = null));
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.LowStockCount.Should().Be(0,
                "chưa cấu hình ngưỡng đặt lại thì không thể kết luận là tồn thấp");
        }

        [Fact]
        public async Task Report_TopLowStockCapsAtFiveSortedByAvailableAscending()
        {
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            for (var qty = 6; qty >= 1; qty--)
            {
                var p = TestData.SeedProduct(_db);
                _db.Inventories.Add(TestData.Inventory(p.Id, loc.Id, qty, i => i.ReorderThreshold = 100));
            }
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.LowStockCount.Should().Be(6);
            report.TopLowStockItems.Should().HaveCount(5, "chỉ lấy 5 mã thiếu nhất");
            report.TopLowStockItems.First().AvailableQuantity.Should().Be(1, "thiếu nhất đứng đầu");
        }

        [Fact]
        public async Task Report_GroupsByCategoryAndBucketsMaterialsSeparately()
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = 200m);
            var material = SeedMaterial();
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.AddRange(
                TestData.Inventory(product.Id, loc.Id, 4),
                new Inventory { MaterialId = material.Id, WarehouseLocationId = loc.Id, OnHandQuantity = 9 });
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, null, null);

            report.CategoryBreakdown.Should().HaveCount(2);
            report.CategoryBreakdown.First().TotalValue.Should().Be(800m,
                "nhóm có giá trị lớn nhất đứng đầu");
            report.CategoryBreakdown.Should().Contain(c => c.CategoryName == "Nguyên vật liệu" && c.TotalValue == 0);
        }

        [Fact]
        public async Task Report_SplitsStockMovementIntoInAndOutPerDay()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            var day = DateTime.UtcNow.Date.AddDays(-2);
            _db.StockTransactions.AddRange(
                Tx(inv.Id, loc.Id, +30, TransactionType.GoodsReceipt, day.AddHours(8)),
                Tx(inv.Id, loc.Id, +20, TransactionType.GoodsReceipt, day.AddHours(9)),
                Tx(inv.Id, loc.Id, -12, TransactionType.GoodsIssue, day.AddHours(10)));
            await _db.SaveChangesAsync();

            var report = await _sut.GetInventoryReportAsync(null, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow);

            var point = report.StockMovement.Single(p => p.Date == day);
            point.TotalIn.Should().Be(50);
            point.TotalOut.Should().Be(12, "lượng xuất phải là số dương, không phải -12");
        }

        [Fact]
        public async Task Report_DaysWithoutTransactions_ReportZeroNotMissing()
        {
            var report = await _sut.GetInventoryReportAsync(null, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow);

            report.StockMovement.Should().HaveCount(4);
            report.StockMovement.Should().OnlyContain(p => p.TotalIn == 0 && p.TotalOut == 0,
                "ngày không có giao dịch vẫn phải có điểm 0 để biểu đồ không đứt đoạn");
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetSlowMovingItemsAsync + BuildSlowMovingSuggestion
        // ═══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task SlowMoving_WhenDaysNotPositive_Rejected()
        {
            var act = () => _sut.GetSlowMovingItemsAsync(null, 0);

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*lớn hơn 0*");
        }

        [Fact]
        public async Task SlowMoving_IgnoresItemsWithNoStockLeft()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.Add(TestData.Inventory(product.Id, loc.Id, 0));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(null, 14);

            result.Should().BeEmpty("hết hàng là hết hàng, không phải chậm luân chuyển");
        }

        [Fact]
        public async Task SlowMoving_ExcludesItemIssuedRecently()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            _db.StockTransactions.Add(Tx(inv.Id, loc.Id, -3, TransactionType.GoodsIssue, DateTime.UtcNow.AddDays(-2)));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(null, 14);

            result.Should().BeEmpty("vừa xuất cách đây 2 ngày, chưa quá ngưỡng 14 ngày");
        }

        [Fact]
        public async Task SlowMoving_ItemNeverIssued_HasNullDaysAndSortsFirst()
        {
            var never = TestData.SeedProduct(_db, p => p.Name = "Chua tung xuat");
            var old = TestData.SeedProduct(_db, p => p.Name = "Xuat lau roi");
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var invNever = TestData.Inventory(never.Id, loc.Id, 5);
            var invOld = TestData.Inventory(old.Id, loc.Id, 5);
            _db.Inventories.AddRange(invNever, invOld);
            _db.StockTransactions.Add(Tx(invOld.Id, loc.Id, -1, TransactionType.GoodsIssue, DateTime.UtcNow.AddDays(-40)));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(null, 14);

            result.Should().HaveCount(2);
            result.First().ItemName.Should().Be("Chua tung xuat",
                "chưa từng xuất được coi là tệ nhất, xếp đầu danh sách");
            result.First().DaysSinceLastOutbound.Should().BeNull();
            result.First().LastOutboundAt.Should().BeNull();
            result.Last().DaysSinceLastOutbound.Should().Be(40);
        }

        [Fact]
        public async Task SlowMoving_PositiveGoodsIssueIsReversalNotAnOutbound()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            // Phiếu đảo (Reversal) là nhập lại kho — không được coi là "hàng đã rời kho".
            _db.StockTransactions.Add(Tx(inv.Id, loc.Id, +5, TransactionType.GoodsIssue, DateTime.UtcNow.AddHours(-1)));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(null, 14);

            result.Should().ContainSingle().Which.DaysSinceLastOutbound.Should().BeNull();
        }

        [Fact]
        public async Task SlowMoving_WithWarehouseFilter_ExcludesOtherWarehouses()
        {
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.Inventories.AddRange(
                TestData.Inventory(p1.Id, loc1.Id, 5),
                TestData.Inventory(p2.Id, loc2.Id, 5));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(w1.Id, 14);

            result.Should().ContainSingle().Which.ProductId.Should().Be(p1.Id);
        }

        [Theory]
        // Sản phẩm: 4 mốc gợi ý
        [InlineData(false, 70, "Cân nhắc thanh lý hoặc tái chế")]
        [InlineData(false, 40, "Báo Marketing xây dựng chiến dịch")]
        [InlineData(false, 20, "Giảm giá khuyến mãi")]
        // Nguyên vật liệu: 4 mốc gợi ý khác hẳn
        [InlineData(true, 70, "Chuyển dùng cho đơn hàng khác")]
        [InlineData(true, 40, "Xuất sử dụng nội bộ")]
        [InlineData(true, 20, "Kiểm tra nhu cầu sản xuất")]
        public async Task SlowMoving_SuggestionDependsOnItemTypeAndAge(
            bool isMaterial, int daysAgo, string expectedSuggestion)
        {
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);

            Inventory inv;
            if (isMaterial)
            {
                var material = SeedMaterial();
                inv = new Inventory { MaterialId = material.Id, WarehouseLocationId = loc.Id, OnHandQuantity = 5 };
            }
            else
            {
                var product = TestData.SeedProduct(_db);
                inv = TestData.Inventory(product.Id, loc.Id, 5);
            }
            _db.Inventories.Add(inv);
            _db.StockTransactions.Add(Tx(inv.Id, loc.Id, -1, TransactionType.GoodsIssue, DateTime.UtcNow.AddDays(-daysAgo)));
            await _db.SaveChangesAsync();

            var result = await _sut.GetSlowMovingItemsAsync(null, 14);

            var item = result.Should().ContainSingle().Subject;
            item.Suggestion.Should().Be(expectedSuggestion);
            item.ItemType.Should().Be(isMaterial ? "Material" : "Product");
        }

        [Fact]
        public async Task SlowMoving_MaterialFallsBackToMaterialNameAndUnit()
        {
            var material = SeedMaterial("Lõi giấy");
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.Inventories.Add(new Inventory
            {
                MaterialId = material.Id,
                WarehouseLocationId = loc.Id,
                OnHandQuantity = 5
            });
            await _db.SaveChangesAsync();

            var item = (await _sut.GetSlowMovingItemsAsync(null, 14)).Should().ContainSingle().Subject;

            item.ItemName.Should().Be("Lõi giấy");
            item.Unit.Should().Be("Cuộn");
            item.Sku.Should().BeEmpty("nguyên vật liệu không có SKU");
        }

    }
}
