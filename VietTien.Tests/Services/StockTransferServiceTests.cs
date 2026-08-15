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
    /// Sheet: StockTransferService — L1-ST-01..06. EF InMemory + mock IEmailService/ICloudinaryService.
    /// </summary>
    public class StockTransferServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IEmailService> _email = new();
        private readonly Mock<INotificationService> _notification = new();
        private readonly StockTransferService _sut;
        private readonly User _staff;
        private readonly Warehouse _w1;
        private readonly WarehouseLocation _loc1;
        private readonly Warehouse _w2;
        private readonly WarehouseLocation _loc2;
        private readonly Product _p1;

        public StockTransferServiceTests()
        {
            _sut = new StockTransferService(_db, _email.Object, new Mock<ICloudinaryService>().Object, _notification.Object, TestWarehouseAccessGuard.Create(_db), new NoOpAuditLogService());
            _staff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _db.Users.Add(_staff);
            (_w1, _loc1) = TestData.Warehouse();
            (_w2, _loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(_w1, _w2);
            _p1 = TestData.SeedProduct(_db);
            _staff.AssignedWarehouseId = _w2.Id; // gán vào kho đích -> được phép nhận hàng ở W2 (L1-ST-05/06)
            _db.SaveChanges();
        }

        private async Task<StockTransferDto> CreateDraft(int qty = 5)
        {
            return await _sut.CreateAsync(new CreateStockTransferDto
            {
                SourceWarehouseId = _w1.Id,
                DestinationWarehouseId = _w2.Id,
                Items = new List<CreateStockTransferItemDto> { new() { ProductId = _p1.Id, Quantity = qty } }
            }, _staff.Id);
        }

        // L1-ST-01 | EP-Valid | Tạo phiếu điều chuyển W1 -> W2 -> status Draft với 1 item
        [Fact]
        public async Task L1_ST_01_Create_DraftBetweenTwoWarehouses()
        {
            var dto = await CreateDraft(qty: 5);

            dto.Status.Should().Be(StockTransferStatus.Draft);
            dto.Items.Should().ContainSingle(i => i.ProductId == _p1.Id && i.Quantity == 5);
            _db.StockTransfers.Count().Should().Be(1);
        }

        // L1-ST-02 | Guard-FALSE | Tạo phiếu với kho xuất == kho nhập -> reject, không tạo phiếu
        [Fact]
        public async Task L1_ST_02_Create_SameSourceAndDestination_Rejected()
        {
            var act = () => _sut.CreateAsync(new CreateStockTransferDto
            {
                SourceWarehouseId = _w1.Id,
                DestinationWarehouseId = _w1.Id,
                Items = new List<CreateStockTransferItemDto> { new() { ProductId = _p1.Id, Quantity = 5 } }
            }, _staff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Kho xuất và kho nhập không được trùng nhau.");
            _db.StockTransfers.Count().Should().Be(0);
        }

        private void SeedSourceStock(int onHand)
        {
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _loc1.Id, onHand));
            _db.SaveChanges();
        }

        // L1-ST-03 | State-Valid | Dispatch phiếu Draft -> kho xuất trừ OnHand, cộng InTransit, status Dispatched
        [Fact]
        public async Task L1_ST_03_Dispatch_Draft_SourceStockDecremented()
        {
            SeedSourceStock(10); // tồn W1 = 10 (loc1)
            var draft = await CreateDraft(qty: 5);

            var dto = await _sut.DispatchAsync(draft.Id);

            dto.Status.Should().Be(StockTransferStatus.Dispatched);
            var inv = _db.Inventories.Single(i => i.WarehouseLocationId == _loc1.Id);
            inv.OnHandQuantity.Should().Be(5);
            inv.InTransitQuantity.Should().Be(5);
        }

        // L1-ST-04 | State-Invalid | Dispatch phiếu không ở Draft -> conflict, tồn kho không đổi
        [Fact]
        public async Task L1_ST_04_Dispatch_NonDraft_Rejected()
        {
            SeedSourceStock(10);
            var draft = await CreateDraft(qty: 5);
            await _sut.DispatchAsync(draft.Id); // Draft -> Dispatched

            var act = () => _sut.DispatchAsync(draft.Id); // lần 2

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Chỉ có thể xuất kho cho phiếu ở trạng thái Nháp hoặc Đã xếp xe.");
            _db.Inventories.Single(i => i.WarehouseLocationId == _loc1.Id).OnHandQuantity.Should().Be(5); // không trừ đôi
        }

        // L1-ST-05 | State-Valid | Receive phiếu Dispatched -> kho nhập cộng đúng số nhận, status Received (terminal)
        [Fact]
        public async Task L1_ST_05_Receive_Dispatched_DestinationIncremented()
        {
            SeedSourceStock(10);
            _db.Inventories.Add(TestData.Inventory(_p1.Id, _loc2.Id, 2)); // tồn W2 = 2
            _db.SaveChanges();
            var draft = await CreateDraft(qty: 5);
            await _sut.DispatchAsync(draft.Id);

            var dto = await _sut.ReceiveAsync(draft.Id, new ReceiveStockTransferDto
            {
                ItemsJson = $"[{{\"productId\":\"{_p1.Id}\",\"receivedQuantity\":5}}]"
            }, _staff.Id);

            dto.Status.Should().Be(StockTransferStatus.Received);
            _db.Inventories.Single(i => i.WarehouseLocationId == _loc2.Id).OnHandQuantity.Should().Be(7); // 2 + 5
            _db.Inventories.Single(i => i.WarehouseLocationId == _loc1.Id).InTransitQuantity.Should().Be(0);
        }

        // L1-ST-06 | Guard-FALSE | Nhận với ReceivedQuantity > số lượng đã gửi -> reject, tồn kho không đổi
        [Fact]
        public async Task L1_ST_06_Receive_MoreThanDispatched_Rejected()
        {
            SeedSourceStock(10);
            var draft = await CreateDraft(qty: 5);
            await _sut.DispatchAsync(draft.Id);

            var act = () => _sut.ReceiveAsync(draft.Id, new ReceiveStockTransferDto
            {
                ItemsJson = $"[{{\"productId\":\"{_p1.Id}\",\"receivedQuantity\":500}}]"
            }, _staff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("*không được vượt quá số lượng đã xuất*");
            _db.StockTransfers.Single(t => t.Id == draft.Id).Status.Should().Be(StockTransferStatus.Dispatched);
        }

        // P1: trước đây GetAllAsync() không nhận/đọc status -> FE lọc kiểu gì cũng luôn trả toàn bộ danh sách.
        [Fact]
        public async Task P1_GetAll_FilterByStatus_OnlyReturnsMatchingTransfers()
        {
            SeedSourceStock(20);
            var draftOnly = await CreateDraft(qty: 3);
            var dispatched = await CreateDraft(qty: 5);
            await _sut.DispatchAsync(dispatched.Id);

            var dispatchedOnly = await _sut.GetAllAsync("Dispatched");
            var draftOnlyResult = await _sut.GetAllAsync("draft"); // không phân biệt hoa/thường
            var all = await _sut.GetAllAsync();

            dispatchedOnly.Should().ContainSingle(t => t.Id == dispatched.Id);
            draftOnlyResult.Should().ContainSingle(t => t.Id == draftOnly.Id);
            all.Should().HaveCount(2, "không truyền status thì phải trả về toàn bộ, không lọc gì cả");
        }

        [Fact]
        public async Task P1_GetAll_InvalidStatusString_IsIgnored_ReturnsAll()
        {
            SeedSourceStock(20);
            await CreateDraft(qty: 3);

            var result = await _sut.GetAllAsync("khong-phai-trang-thai-hop-le");

            result.Should().HaveCount(1, "status không hợp lệ phải bị bỏ qua thay vì làm rỗng kết quả hoặc lỗi 500");
        }

        // L1-REG-03 | EP-Invalid | IDOR StockTransferService: nhận hàng ở kho không phải kho đích được gán.
        // ĐÃ SỬA: ReceiveAsync giờ nhận staffId, đối chiếu AssignedWarehouseId với DestinationWarehouseId
        // (cùng cơ chế với L1-REG-01/InventoryService.AdjustInventoryAsync).
        [Fact]
        public async Task L1_REG_03_Receive_StaffNotAssignedToDestinationWarehouse_Forbidden()
        {
            SeedSourceStock(10);
            var draft = await CreateDraft(qty: 5);
            await _sut.DispatchAsync(draft.Id);

            var otherStaff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            otherStaff.AssignedWarehouseId = _w1.Id; // gán kho W1 (kho NGUỒN), không phải W2 (kho ĐÍCH)
            _db.Users.Add(otherStaff);
            _db.SaveChanges();

            var act = () => _sut.ReceiveAsync(draft.Id, new ReceiveStockTransferDto
            {
                ItemsJson = $"[{{\"productId\":\"{_p1.Id}\",\"receivedQuantity\":5}}]"
            }, otherStaff.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _db.StockTransfers.Single(t => t.Id == draft.Id).Status.Should().Be(StockTransferStatus.Dispatched); // chưa đổi sang Received
            _db.Inventories.Where(i => i.WarehouseLocationId == _loc2.Id).Should().BeEmpty("chưa hề cộng tồn cho kho đích W2");
        }
    }
}
