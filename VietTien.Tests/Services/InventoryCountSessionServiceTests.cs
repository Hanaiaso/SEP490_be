using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Repositories.Implementations;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// DEF-L4-003: Phiên kiểm kê tồn kho. Ngưỡng tự động áp dụng mặc định trong test = 5 đơn vị
    /// (mock ISystemConfigService, không phụ thuộc seed data thật).
    /// </summary>
    public class InventoryCountSessionServiceTests
    {
        private readonly ApplicationDbContext _db;
        private readonly Mock<ISystemConfigService> _sysConfig = new();
        private readonly Mock<INotificationService> _noti = new();
        private readonly InventoryCountSessionService _sut;

        public InventoryCountSessionServiceTests()
        {
            _db = TestDbFactory.Create();
            _sysConfig.Setup(s => s.GetEffectiveValueAsync("INVENTORY_COUNT_VARIANCE_THRESHOLD", It.IsAny<DateTime?>()))
                      .ReturnsAsync("5");
            _sut = new InventoryCountSessionService(_db, _sysConfig.Object, _noti.Object, TestWarehouseAccessGuard.Create(_db));
        }

        private (Warehouse warehouse, WarehouseLocation location) SeedWarehouse()
        {
            var (w, loc) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            _db.SaveChanges();
            return (w, loc);
        }

        private (User staff, User ceo) SeedStaffAndCeo(Guid warehouseId)
        {
            var (staff, _) = TestData.SeedCustomer(_db, u => { u.Role = SystemRole.WarehouseStaff; u.AssignedWarehouseId = warehouseId; });
            var (ceo, _) = TestData.SeedCustomer(_db, u => u.Role = SystemRole.CEO);
            return (staff, ceo);
        }

        // L1-ICS-01 | EP-Valid | Mở phiên -> snapshot đúng SystemQuantity cho mọi dòng tồn kho của kho
        [Fact]
        public async Task L1_ICS_01_Open_SnapshotsSystemQuantityForAllInventoryInWarehouse()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.Inventories.Add(TestData.Inventory(p2.Id, loc.Id, 50));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);

            var dto = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });

            dto.Status.Should().Be("Open");
            dto.Items.Should().HaveCount(2);
            dto.Items.Select(i => i.SystemQuantity).Should().BeEquivalentTo(new[] { 100, 50 });
            dto.Items.Should().OnlyContain(i => i.PhysicalQuantity == null);
        }

        // L1-ICS-02 | BC-TRUE | Kho đã có phiên Open -> mở phiên thứ 2 bị chặn
        [Fact]
        public async Task L1_ICS_02_Open_WarehouseAlreadyHasOpenSession_Throws()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 10));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });

            var act = () => _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đang có một phiên kiểm kê*");
        }

        // L1-ICS-03 | EP-Valid | Nhập số đếm thực tế cho 1 dòng -> lưu đúng PhysicalQuantity + Note
        [Fact]
        public async Task L1_ICS_03_RecordItemCount_SetsPhysicalQuantityAndNote()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;

            var updated = await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 97, Note = "Thiếu 3" });

            var item = updated.Items.Single();
            item.PhysicalQuantity.Should().Be(97);
            item.Variance.Should().Be(-3);
            item.Note.Should().Be("Thiếu 3");
        }

        // L1-ICS-04 | BC-TRUE | Còn dòng chưa đếm -> đóng phiên bị chặn
        [Fact]
        public async Task L1_ICS_04_Close_UncountedItemsRemaining_Throws()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var p2 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.Inventories.Add(TestData.Inventory(p2.Id, loc.Id, 50));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var firstItem = session.Items.First();
            await _sut.RecordItemCountAsync(session.Id, firstItem.Id, staff.Id, new RecordCountItemRequest { PhysicalQuantity = firstItem.SystemQuantity });

            var act = () => _sut.CloseAsync(session.Id, staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*chưa nhập số đếm*");
        }

        // L1-ICS-05 | BVA | Chênh lệch trong ngưỡng (5) -> áp dụng thẳng vào Inventory, không tạo StockAdjustment
        [Fact]
        public async Task L1_ICS_05_Close_VarianceWithinThreshold_AutoAppliesToInventory()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(p1.Id, loc.Id, 100);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;
            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 103 }); // +3, trong ngưỡng 5

            var result = await _sut.CloseAsync(session.Id, staff.Id);

            result.AutoAppliedCount.Should().Be(1);
            result.PendingApprovalCount.Should().Be(0);
            result.Session.Items.Single().AutoApplied.Should().BeTrue();
            result.Session.Items.Single().StockAdjustmentId.Should().BeNull();
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(103);
            _db.StockTransactions.Should().ContainSingle(t => t.InventoryId == inv.Id && t.QuantityChange == 3);
            _db.StockAdjustments.Should().BeEmpty();
        }

        // L1-ICS-06 | BVA | Chênh lệch vượt ngưỡng (5) -> tạo StockAdjustment Pending, Inventory KHÔNG đổi
        [Fact]
        public async Task L1_ICS_06_Close_VarianceExceedsThreshold_CreatesPendingStockAdjustment_InventoryUnchanged()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(p1.Id, loc.Id, 100);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;
            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 80 }); // -20, vượt ngưỡng 5

            var result = await _sut.CloseAsync(session.Id, staff.Id);

            result.AutoAppliedCount.Should().Be(0);
            result.PendingApprovalCount.Should().Be(1);
            var resultItem = result.Session.Items.Single();
            resultItem.AutoApplied.Should().BeFalse();
            resultItem.StockAdjustmentId.Should().NotBeNull();

            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(100); // chưa đổi

            var adjustment = _db.StockAdjustments.Single();
            adjustment.Status.Should().Be(StockAdjustmentStatus.Pending);
            adjustment.SystemQuantity.Should().Be(100);
            adjustment.PhysicalQuantity.Should().Be(80);
            adjustment.Variance.Should().Be(-20);
        }

        // L1-ICS-07 | BVA | Chênh lệch đúng bằng ngưỡng (5) -> vẫn áp dụng thẳng (dùng <=, không phải <)
        [Fact]
        public async Task L1_ICS_07_Close_VarianceExactlyAtThreshold_AutoApplies()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(p1.Id, loc.Id, 100);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;
            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 95 }); // -5, đúng ngưỡng

            var result = await _sut.CloseAsync(session.Id, staff.Id);

            result.AutoAppliedCount.Should().Be(1);
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(95);
        }

        // L1-ICS-08 | EP-Valid | Số đếm khớp hệ thống (chênh lệch 0) -> không ghi StockTransaction, không tạo StockAdjustment
        [Fact]
        public async Task L1_ICS_08_Close_ZeroVariance_NoTransactionNoAdjustment()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(p1.Id, loc.Id, 100);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;
            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 100 });

            var result = await _sut.CloseAsync(session.Id, staff.Id);

            result.AutoAppliedCount.Should().Be(0);
            result.PendingApprovalCount.Should().Be(0);
            _db.StockTransactions.Should().BeEmpty();
            _db.StockAdjustments.Should().BeEmpty();
        }

        // L1-ICS-09 | BC-TRUE | Số lý thuyết đã khoá lúc mở phiên — biến động tồn kho sau đó (giả lập
        // transaction khác) KHÔNG ảnh hưởng SystemQuantity dùng để tính chênh lệch khi đóng phiên
        [Fact]
        public async Task L1_ICS_09_Close_UsesSystemQuantityLockedAtOpenTime_NotCurrentValue()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            var inv = TestData.Inventory(p1.Id, loc.Id, 100);
            _db.Inventories.Add(inv);
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;

            // Biến động tồn kho khác xảy ra SAU khi phiên đã mở (SystemQuantity phải giữ nguyên 100)
            inv.OnHandQuantity = 70;
            _db.SaveChanges();

            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 100 });
            var result = await _sut.CloseAsync(session.Id, staff.Id);

            // So với SystemQuantity khoá (100): chênh lệch = 0 -> không auto-apply, không tạo StockAdjustment.
            result.AutoAppliedCount.Should().Be(0);
            result.PendingApprovalCount.Should().Be(0);
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(70); // giữ nguyên biến động khác, không bị ghi đè
        }

        // L1-ICS-10 | BC-TRUE | Phiên đã Closed -> đóng lại lần 2 bị chặn
        [Fact]
        public async Task L1_ICS_10_Close_AlreadyClosed_Throws()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            await _sut.RecordItemCountAsync(session.Id, session.Items.Single().Id, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 100 });
            await _sut.CloseAsync(session.Id, staff.Id);

            var act = () => _sut.CloseAsync(session.Id, staff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đóng trước đó*");
        }

        // L1-ICS-11 | EP-Invalid | Sửa số đếm sau khi phiên đã đóng -> bị chặn
        [Fact]
        public async Task L1_ICS_11_RecordItemCount_SessionClosed_Throws()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.SaveChanges();
            var (staff, _) = SeedStaffAndCeo(wh.Id);
            var session = await _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });
            var itemId = session.Items.Single().Id;
            await _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 100 });
            await _sut.CloseAsync(session.Id, staff.Id);

            var act = () => _sut.RecordItemCountAsync(session.Id, itemId, staff.Id, new RecordCountItemRequest { PhysicalQuantity = 90 });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*đã đóng*");
        }

        // L1-ICS-12 | EP-Invalid | WarehouseStaff mở phiên cho kho KHÔNG được gán -> bị chặn
        [Fact]
        public async Task L1_ICS_12_Open_StaffNotAssignedToWarehouse_Throws()
        {
            var (wh, loc) = SeedWarehouse();
            var p1 = TestData.SeedProduct(_db);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc.Id, 100));
            _db.SaveChanges();
            var (otherWarehouse, _) = SeedWarehouse();
            var (staff, _) = TestData.SeedCustomer(_db, u => { u.Role = SystemRole.WarehouseStaff; u.AssignedWarehouseId = otherWarehouse.Id; });

            var act = () => _sut.OpenAsync(staff.Id, new OpenInventoryCountSessionRequest { WarehouseId = wh.Id });

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
