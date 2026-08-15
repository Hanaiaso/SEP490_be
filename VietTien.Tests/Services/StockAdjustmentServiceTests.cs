using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: StockAdjustmentService — P0-1 (Kiểm kê kho -> CEO duyệt điều chỉnh tồn kho, UC-47/UC-54).
    /// EF InMemory + mock INotificationService.
    /// </summary>
    public class StockAdjustmentServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly StockAdjustmentService _sut;
        private readonly User _staff;
        private readonly User _staff2;
        private readonly User _ceo;
        private readonly Product _product;
        private readonly Inventory _inventory;

        public StockAdjustmentServiceTests()
        {
            _sut = new StockAdjustmentService(_db, new Mock<INotificationService>().Object, new NoOpAuditLogService());

            _staff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _staff2 = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _ceo = TestData.User(u => u.Role = SystemRole.CEO);
            _db.Users.AddRange(_staff, _staff2, _ceo);

            _product = TestData.SeedProduct(_db);
            _inventory = TestData.SeedInventory(_db, _product.Id, 100);

            // Gán kho cho staff để pass check "AssignedWarehouseId" trong CreateAsync
            var warehouseId = _db.WarehouseLocations.Single(l => l.Id == _inventory.WarehouseLocationId).WarehouseId;
            _staff.AssignedWarehouseId = warehouseId;
            _staff2.AssignedWarehouseId = warehouseId;
            _db.SaveChanges();
        }

        private CreateStockAdjustmentRequest CreateReq(int physicalQty, string reason = "Kiểm kê định kỳ")
            => new CreateStockAdjustmentRequest { InventoryId = _inventory.Id, PhysicalQuantity = physicalQty, Reason = reason };

        // Tạo đề xuất -> Pending, Variance/SystemQuantity tính đúng theo tồn kho tại thời điểm tạo
        [Fact]
        public async Task Create_Pending_VarianceAndSystemQuantityComputedCorrectly()
        {
            var dto = await _sut.CreateAsync(_staff.Id, CreateReq(97));

            dto.Status.Should().Be(nameof(StockAdjustmentStatus.Pending));
            dto.SystemQuantity.Should().Be(100);
            dto.PhysicalQuantity.Should().Be(97);
            dto.Variance.Should().Be(-3);

            _db.Inventories.Single(i => i.Id == _inventory.Id).OnHandQuantity.Should().Be(100, "chưa duyệt thì tồn kho không được đổi");
        }

        // Approve -> cộng delta vào OnHandQuantity HIỆN TẠI (không ghi đè tuyệt đối bằng PhysicalQuantity),
        // ghi đúng 1 StockTransaction trỏ về đề xuất.
        [Fact]
        public async Task Decide_Approve_AppliesDeltaNotAbsoluteAndWritesStockTransaction()
        {
            var created = await _sut.CreateAsync(_staff.Id, CreateReq(97)); // Variance = -3

            // Giả lập có nhập kho khác xảy ra giữa lúc đề xuất và lúc duyệt (OnHand tăng lên 110)
            var inv = _db.Inventories.Single(i => i.Id == _inventory.Id);
            inv.OnHandQuantity = 110;
            _db.SaveChanges();

            var result = await _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Approved" });

            result.Status.Should().Be(nameof(StockAdjustmentStatus.Approved));
            _db.Inventories.Single(i => i.Id == _inventory.Id).OnHandQuantity.Should().Be(107, "phải là 110 + (-3), không phải ghi đè bằng 97");

            var txs = _db.StockTransactions.Where(t => t.ReferenceId == created.Id).ToList();
            txs.Should().ContainSingle();
            txs[0].QuantityChange.Should().Be(-3);
            txs[0].TransactionType.Should().Be(TransactionType.StockAdjustment);
        }

        // Approve khi delta khiến tồn kho âm -> từ chối, không đổi OnHandQuantity, không ghi StockTransaction
        [Fact]
        public async Task Decide_Approve_WouldGoNegative_Rejected()
        {
            var created = await _sut.CreateAsync(_staff.Id, CreateReq(0)); // Variance = -100

            var inv = _db.Inventories.Single(i => i.Id == _inventory.Id);
            inv.OnHandQuantity = 5; // đã bị tiêu hao gần hết trước khi CEO kịp duyệt
            _db.SaveChanges();

            var act = () => _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Approved" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.Inventories.Single(i => i.Id == _inventory.Id).OnHandQuantity.Should().Be(5);
            _db.StockTransactions.Should().BeEmpty();
        }

        // Reject thiếu ghi chú -> từ chối ở tầng service, trạng thái đề xuất không đổi
        [Fact]
        public async Task Decide_Reject_MissingNote_Rejected()
        {
            var created = await _sut.CreateAsync(_staff.Id, CreateReq(97));

            var act = () => _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Rejected", Note = "  " });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.StockAdjustments.Single(a => a.Id == created.Id).Status.Should().Be(StockAdjustmentStatus.Pending);
        }

        // Reject hợp lệ -> Status = Rejected, tồn kho không đổi
        [Fact]
        public async Task Decide_Reject_WithNote_Succeeds_NoInventoryChange()
        {
            var created = await _sut.CreateAsync(_staff.Id, CreateReq(97));

            var result = await _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Rejected", Note = "Chênh lệch quá nhỏ, không cần điều chỉnh." });

            result.Status.Should().Be(nameof(StockAdjustmentStatus.Rejected));
            _db.Inventories.Single(i => i.Id == _inventory.Id).OnHandQuantity.Should().Be(100);
        }

        // Duyệt đề xuất đã được xử lý -> từ chối (409 ở tầng Controller)
        [Fact]
        public async Task Decide_AlreadyDecided_Rejected()
        {
            var created = await _sut.CreateAsync(_staff.Id, CreateReq(97));
            await _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Approved" });

            var act = () => _sut.DecideAsync(created.Id, _ceo.Id, new StockAdjustmentDecisionRequest { Decision = "Approved" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // WarehouseStaff chỉ thấy đề xuất của chính mình; CEO thấy tất cả
        [Fact]
        public async Task GetList_ScopedByRole()
        {
            await _sut.CreateAsync(_staff.Id, CreateReq(97));
            await _sut.CreateAsync(_staff2.Id, CreateReq(95));

            var staffView = await _sut.GetListAsync(_staff.Id, SystemRole.WarehouseStaff, null);
            var ceoView = await _sut.GetListAsync(_ceo.Id, SystemRole.CEO, null);

            staffView.Should().ContainSingle(a => a.ProposedByUserId == _staff.Id);
            ceoView.Should().HaveCount(2);
        }
    }
}
