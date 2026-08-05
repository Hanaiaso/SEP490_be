using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Hubs;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: InventoryService — L1-INV-01..05. EF InMemory + mock IHubContext&lt;WarehouseHub&gt;.
    /// Lệch spec (ghi Notes): AdjustInventoryAsync không ghi StockTransaction audit row (chỉ cập nhật
    /// LastUpdatedBy/LastUpdatedAt).
    /// </summary>
    public class InventoryServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly InventoryService _sut;

        public InventoryServiceTests()
        {
            _sut = new InventoryService(_db, MockHubContext.Create<WarehouseHub>().Object, new Mock<ILogger<InventoryService>>().Object);
        }

        // L1-INV-01 | EP-Valid | Lọc theo kho + từ khóa -> chỉ trả dòng khớp trong đúng kho
        [Fact]
        public async Task L1_INV_01_GetByWarehouse_FilterAndSearch()
        {
            var bag = TestData.SeedProduct(_db, p => p.Name = "Túi bag lớn");
            var pipe = TestData.SeedProduct(_db, p => p.Name = "Ống nhựa");
            var other = TestData.SeedProduct(_db, p => p.Name = "Túi bag nhỏ");

            var (w1, loc1) = TestData.Warehouse();
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.AddRange(w1, w2);
            _db.Inventories.AddRange(
                TestData.Inventory(bag.Id, loc1.Id, 10),
                TestData.Inventory(pipe.Id, loc1.Id, 20),
                TestData.Inventory(other.Id, loc2.Id, 30)); // 'bag' nhưng ở kho W2
            _db.SaveChanges();

            var result = await _sut.GetInventoryByWarehouseAsync(w1.Id, "bag", null, null, null, null, 1, 20);

            result.TotalCount.Should().Be(1);
            result.Items.Single().ProductId.Should().Be(bag.Id);
        }

        // L1-INV-02 | EP-Valid | Điều chỉnh số lượng -> OnHandQuantity mới + ghi nhận người/thời điểm cập nhật
        // (Lệch spec: không có bảng StockTransaction audit cho thao tác này — ghi Notes.)
        [Fact]
        public async Task L1_INV_02_Adjust_UpdatesQuantityAndAuditFields()
        {
            var p1 = TestData.SeedProduct(_db);
            var staff = TestData.User();
            _db.Users.Add(staff);
            var inv = TestData.SeedInventory(_db, p1.Id, 10);

            await _sut.AdjustInventoryAsync(inv.Id, 7, "stocktake", staff.Id);

            var updated = _db.Inventories.Single(i => i.Id == inv.Id);
            updated.OnHandQuantity.Should().Be(7);
            updated.LastUpdatedByUserId.Should().Be(staff.Id);
            updated.LastUpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        }

        // L1-INV-03 | Guard-FALSE | Điều chỉnh newQuantity < 0 -> reject (BV-02: tồn kho >= 0), số lượng không đổi
        [Fact]
        public async Task L1_INV_03_Adjust_NegativeQuantity_Rejected()
        {
            var p1 = TestData.SeedProduct(_db);
            var staff = TestData.User();
            _db.Users.Add(staff);
            var inv = TestData.SeedInventory(_db, p1.Id, 10);

            var act = () => _sut.AdjustInventoryAsync(inv.Id, -1, "stocktake", staff.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("*không được âm*");
            _db.Inventories.Single(i => i.Id == inv.Id).OnHandQuantity.Should().Be(10);
        }

        // L1-INV-04 | EP-Valid | Thêm sản phẩm vào kho -> tạo dòng inventory với đúng số lượng ban đầu
        [Fact]
        public async Task L1_INV_04_AddProductToWarehouse_RowCreated()
        {
            var p1 = TestData.SeedProduct(_db);
            var staff = TestData.User();
            _db.Users.Add(staff);
            var (w2, loc2) = TestData.Warehouse();
            _db.Warehouses.Add(w2);
            _db.SaveChanges();

            var dto = await _sut.AddProductToWarehouseAsync(new AddInventoryRequest
            {
                ProductId = p1.Id,
                WarehouseLocationId = loc2.Id,
                InitialQuantity = 20
            }, staff.Id);

            dto.OnHandQuantity.Should().Be(20);
            _db.Inventories.Should().ContainSingle(i => i.ProductId == p1.Id && i.WarehouseLocationId == loc2.Id && i.OnHandQuantity == 20);
        }

        // L1-INV-05 | EP-Invalid | Sản phẩm đã có dòng inventory tại vị trí đó -> từ chối, không tạo dòng trùng
        [Fact]
        public async Task L1_INV_05_AddProductToWarehouse_Duplicate_Rejected()
        {
            var p1 = TestData.SeedProduct(_db);
            var staff = TestData.User();
            _db.Users.Add(staff);
            var inv = TestData.SeedInventory(_db, p1.Id, 10);

            var act = () => _sut.AddProductToWarehouseAsync(new AddInventoryRequest
            {
                ProductId = p1.Id,
                WarehouseLocationId = inv.WarehouseLocationId,
                InitialQuantity = 5
            }, staff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Mục này đã tồn tại ở vị trí lưu trữ này.*");
            _db.Inventories.Count(i => i.ProductId == p1.Id).Should().Be(1);
        }
    }
}
