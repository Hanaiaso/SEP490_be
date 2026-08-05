using FluentAssertions;
using VietTien.API.DTOs.Material;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: MaterialService — L1-MAT-01..03. EF InMemory.</summary>
    public class MaterialServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly MaterialService _sut;

        public MaterialServiceTests() => _sut = new MaterialService(_db);

        // L1-MAT-01 | EP-Valid | Tạo nguyên liệu hợp lệ -> lưu DB, tồn kho ban đầu = 0
        [Fact]
        public async Task L1_MAT_01_Create_Valid_PersistedWithZeroStock()
        {
            var dto = await _sut.CreateAsync(new CreateMaterialDto { Name = "Nhựa PP", Unit = "Kg", SafetyThreshold = 100 });

            dto.Name.Should().Be("Nhựa PP");
            dto.Unit.Should().Be("Kg");
            dto.CurrentStock.Should().Be(0);
            _db.Materials.Count().Should().Be(1);
        }

        // L1-MAT-02 | EP-Valid | GetAll với search -> lọc theo tên, không phân biệt hoa thường
        [Fact]
        public async Task L1_MAT_02_GetAll_SearchFiltersByName_CaseInsensitive()
        {
            await _sut.CreateAsync(new CreateMaterialDto { Name = "Nhựa PP", Unit = "Kg" });
            await _sut.CreateAsync(new CreateMaterialDto { Name = "Giấy Kraft", Unit = "Cuộn" });

            var result = (await _sut.GetAllAsync("nhựa")).ToList();

            result.Should().ContainSingle().Which.Name.Should().Be("Nhựa PP");
        }

        // L1-MAT-03 | EP-Invalid | Update id không tồn tại -> not found, không upsert
        [Fact]
        public async Task L1_MAT_03_Update_Unknown_RejectedNoUpsert()
        {
            var act = () => _sut.UpdateAsync(Guid.NewGuid(), new UpdateMaterialDto { Name = "X", Unit = "Kg" });

            await act.Should().ThrowAsync<Exception>().WithMessage("Không tìm thấy nguyên liệu.");
            _db.Materials.Count().Should().Be(0);
        }
    }
}
