using FluentAssertions;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: WarehouseManagementService — L1-WM-01..03. EF InMemory.</summary>
    public class WarehouseManagementServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly WarehouseManagementService _sut;

        public WarehouseManagementServiceTests() => _sut = new WarehouseManagementService(_db);

        // L1-WM-01 | EP-Valid | Tạo kho hợp lệ kèm vị trí -> lưu DB, DTO trả đủ thông tin
        [Fact]
        public async Task L1_WM_01_CreateWarehouse_Valid_Persisted()
        {
            var dto = await _sut.CreateWarehouseAsync(new CreateWarehouseDto
            {
                Name = "Kho thành phẩm",
                Code = "WH-PROD",
                LocationNames = new List<string> { "Khu A", "Khu B" }
            });

            dto.Name.Should().Be("Kho thành phẩm");
            dto.Code.Should().Be("WH-PROD");
            dto.Locations.Should().HaveCount(2);
            _db.Warehouses.Count().Should().Be(1);
        }

        // L1-WM-02 | EP-Invalid | Xóa kho còn tồn kho (qty > 0) -> bị chặn, kho và inventory còn nguyên
        [Fact]
        public async Task L1_WM_02_DeleteWarehouse_WithInventory_Blocked()
        {
            var p1 = TestData.SeedProduct(_db);
            var (w1, loc1) = TestData.Warehouse();
            _db.Warehouses.Add(w1);
            _db.Inventories.Add(TestData.Inventory(p1.Id, loc1.Id, 5));
            _db.SaveChanges();

            var act = () => _sut.DeleteWarehouseAsync(w1.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Không thể xóa kho đang chứa hàng tồn kho.*");
            _db.Warehouses.Count().Should().Be(1);
            _db.Inventories.Count().Should().Be(1);
        }

        // L1-WM-03 | EP-Invalid | Update kho không tồn tại -> not found, không tạo mới
        [Fact]
        public async Task L1_WM_03_UpdateWarehouse_Unknown_NotFound()
        {
            var act = () => _sut.UpdateWarehouseAsync(Guid.NewGuid(), new UpdateWarehouseDto { Name = "X", Code = "WH-X" });

            await act.Should().ThrowAsync<Exception>().WithMessage("Không tìm thấy kho hàng.");
            _db.Warehouses.Count().Should().Be(0);
        }
    }
}
