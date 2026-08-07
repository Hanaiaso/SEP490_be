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
                new Mock<ILogger<InventoryService>>().Object);

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

        // ═══════════════════════════════════════════════════════════════════
        // SubmitShiftInventoryCountAsync
        // ═══════════════════════════════════════════════════════════════════

        private ShiftInventoryCountRequestDto CountRequest(Guid warehouseId,
            params (Guid InventoryId, int Actual)[] items) => new()
            {
                WarehouseId = warehouseId,
                CountDate = DateTime.UtcNow,
                Items = items.Select(i => new ShiftInventoryCountItemDto
                {
                    InventoryId = i.InventoryId,
                    ActualQuantity = i.Actual
                }).ToList()
            };

        [Fact]
        public async Task ShiftCount_WhenItemListEmpty_Rejected()
        {
            var act = () => _sut.SubmitShiftInventoryCountAsync(
                new ShiftInventoryCountRequestDto { WarehouseId = Guid.NewGuid(), CountDate = DateTime.UtcNow },
                Guid.NewGuid());

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*không được để trống*");
        }

        [Fact]
        public async Task ShiftCount_WhenSameInventoryCountedTwice_Rejected()
        {
            var id = Guid.NewGuid();
            var act = () => _sut.SubmitShiftInventoryCountAsync(
                CountRequest(Guid.NewGuid(), (id, 5), (id, 7)), Guid.NewGuid());

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*trùng lặp*");
        }

        [Fact]
        public async Task ShiftCount_WhenCountDateInFuture_Rejected()
        {
            var request = CountRequest(Guid.NewGuid(), (Guid.NewGuid(), 5));
            request.CountDate = DateTime.UtcNow.AddDays(3);

            var act = () => _sut.SubmitShiftInventoryCountAsync(request, Guid.NewGuid());

            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*tương lai*");
        }

        [Fact]
        public async Task ShiftCount_AllowsOneDayAheadForTimezoneSkew()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            var request = CountRequest(w.Id, (inv.Id, 10));
            request.CountDate = DateTime.UtcNow.AddDays(1);

            var result = await _sut.SubmitShiftInventoryCountAsync(request, Guid.NewGuid());

            result.TotalCounted.Should().Be(1, "cho phép trễ 1 ngày để bù lệch múi giờ client/server");
        }

        [Fact]
        public async Task ShiftCount_WhenWarehouseMissing_Rejected()
        {
            var act = () => _sut.SubmitShiftInventoryCountAsync(
                CountRequest(Guid.NewGuid(), (Guid.NewGuid(), 5)), Guid.NewGuid());

            await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*không tìm thấy kho*");
        }

        [Fact]
        public async Task ShiftCount_WhenQuantityMatches_NoAdjustmentRecorded()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            var result = await _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 10)), Guid.NewGuid());

            result.TotalCounted.Should().Be(1);
            result.AdjustedCount.Should().Be(0, "đếm khớp tồn hệ thống thì không phát sinh điều chỉnh");
            _db.StockTransactions.Should().BeEmpty();
        }

        [Fact]
        public async Task ShiftCount_WhenShortage_AdjustsDownAndWritesNegativeTransaction()
        {
            var product = TestData.SeedProduct(_db, p => p.Name = "Ống PVC");
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            var staff = TestData.User(u => u.Role = SystemRole.Admin);
            _db.Users.Add(staff);
            await _db.SaveChangesAsync();

            var result = await _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 7)), staff.Id);

            result.AdjustedCount.Should().Be(1);
            var adjusted = result.AdjustedItems.Single();
            adjusted.OldQuantity.Should().Be(10);
            adjusted.NewQuantity.Should().Be(7);
            adjusted.ItemName.Should().Be("Ống PVC");

            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(7);
            var tx = _db.StockTransactions.Should().ContainSingle().Subject;
            tx.QuantityChange.Should().Be(-3, "kiểm kê thiếu 3 phải ghi biến động âm");
            tx.TransactionType.Should().Be(TransactionType.StockAdjustment);
            tx.CreatedByUserId.Should().Be(staff.Id);
        }

        [Fact]
        public async Task ShiftCount_WhenSurplus_WritesPositiveTransaction()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            await _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 15)), Guid.NewGuid());

            _db.StockTransactions.Should().ContainSingle().Which.QuantityChange.Should().Be(5);
        }

        [Fact]
        public async Task ShiftCount_SkipsUnknownInventoryIdWithoutFailingWholeSession()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            var result = await _sut.SubmitShiftInventoryCountAsync(
                CountRequest(w.Id, (inv.Id, 8), (Guid.NewGuid(), 99)), Guid.NewGuid());

            result.TotalCounted.Should().Be(2);
            result.AdjustedCount.Should().Be(1,
                "một dòng rác không được làm hỏng cả phiên kiểm kê");
        }

        [Fact]
        public async Task ShiftCount_SkipsInventoryBelongingToAnotherWarehouse()
        {
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            var inv1 = TestData.Inventory(p1.Id, loc1.Id, 10);
            var inv2 = TestData.Inventory(p2.Id, loc2.Id, 10);
            _db.Inventories.AddRange(inv1, inv2);
            await _db.SaveChangesAsync();

            var result = await _sut.SubmitShiftInventoryCountAsync(
                CountRequest(w1.Id, (inv1.Id, 8), (inv2.Id, 1)), Guid.NewGuid());

            result.AdjustedCount.Should().Be(1);
            _db.Inventories.Single(i => i.Id == inv2.Id).OnHandQuantity.Should().Be(10,
                "dòng thuộc kho khác trong cùng request phải bị bỏ qua, không bị sửa tồn");
        }

        [Fact]
        public async Task ShiftCount_WarehouseStaffCannotCountAnotherWarehouse()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            var staff = TestData.User(u =>
            {
                u.Role = SystemRole.WarehouseStaff;
                u.AssignedWarehouseId = Guid.NewGuid();   // được phân công kho KHÁC
            });
            _db.Users.Add(staff);
            await _db.SaveChangesAsync();

            var act = () => _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 3)), staff.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*không có quyền*");
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(10);
        }

        [Fact]
        public async Task ShiftCount_AdminIsNotRestrictedToAssignedWarehouse()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            var admin = TestData.User(u =>
            {
                u.Role = SystemRole.Admin;
                u.AssignedWarehouseId = Guid.NewGuid();
            });
            _db.Users.Add(admin);
            await _db.SaveChangesAsync();

            var result = await _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 3)), admin.Id);

            result.AdjustedCount.Should().Be(1, "chỉ WarehouseStaff bị giới hạn theo kho được phân công");
        }

        [Fact]
        public async Task ShiftCount_WithShift_PutsShiftLabelInTransactionNote()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            var shift = new WarehouseShift
            {
                Name = "Ca Sáng",
                StartTime = new TimeSpan(6, 0, 0),
                EndTime = new TimeSpan(14, 0, 0)
            };
            _db.WarehouseShifts.Add(shift);
            await _db.SaveChangesAsync();

            var request = CountRequest(w.Id, (inv.Id, 8));
            request.ShiftId = shift.Id;
            request.Items[0].Note = "hang bi am";

            await _sut.SubmitShiftInventoryCountAsync(request, Guid.NewGuid());

            var note = _db.StockTransactions.Single().Note!;
            note.Should().Contain("Ca Sáng").And.Contain("06:00-14:00");
            note.Should().Contain("hang bi am", "ghi chú của người kiểm kê phải được giữ lại");
        }

        [Fact]
        public async Task ShiftCount_WithoutShift_UsesUnknownShiftLabel()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            await _sut.SubmitShiftInventoryCountAsync(CountRequest(w.Id, (inv.Id, 8)), Guid.NewGuid());

            _db.StockTransactions.Single().Note.Should().Contain("ca không xác định");
        }

        [Fact]
        public async Task ShiftCount_WithUnknownShiftId_FallsBackToUnknownLabel()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            var request = CountRequest(w.Id, (inv.Id, 8));
            request.ShiftId = Guid.NewGuid();   // ca không tồn tại

            await _sut.SubmitShiftInventoryCountAsync(request, Guid.NewGuid());

            _db.StockTransactions.Single().Note.Should().Contain("ca không xác định",
                "ShiftId rác không được làm hỏng phiên kiểm kê");
        }

        [Fact]
        public async Task ShiftCount_TruncatesNoteToColumnLimit()
        {
            var product = TestData.SeedProduct(_db);
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var inv = TestData.Inventory(product.Id, loc.Id, 10);
            _db.Inventories.Add(inv);
            await _db.SaveChangesAsync();

            var request = CountRequest(w.Id, (inv.Id, 8));
            request.Items[0].Note = new string('x', 900);

            await _sut.SubmitShiftInventoryCountAsync(request, Guid.NewGuid());

            _db.StockTransactions.Single().Note!.Length.Should().Be(500,
                "cột Note là nvarchar(500) — phải cắt ở tầng service chứ không để DB ném lỗi");
        }
    }
}
