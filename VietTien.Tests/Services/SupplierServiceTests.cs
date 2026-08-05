using FluentAssertions;
using VietTien.API.DTOs.Supplier;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: SupplierService — L1-SUP-01..03. EF InMemory.</summary>
    public class SupplierServiceTests
    {
        private readonly VietTien.API.Data.ApplicationDbContext _db = TestDbFactory.Create();
        private readonly SupplierService _sut;

        public SupplierServiceTests() => _sut = new SupplierService(_db);

        // L1-SUP-01 | EP-Valid | Tạo supplier hợp lệ -> lưu DB, DTO trả về đúng dữ liệu request
        [Fact]
        public async Task L1_SUP_01_Create_Valid_PersistedAndEchoed()
        {
            var dto = await _sut.CreateAsync(new CreateSupplierRequest
            {
                Name = "Công ty Nhựa ABC",
                Code = "SUP-001",
                ContactPerson = "Anh Ba",
                Phone = "0912345678",
                Email = "abc@sup.com"
            });

            dto.Name.Should().Be("Công ty Nhựa ABC");
            dto.Code.Should().Be("SUP-001");
            dto.IsActive.Should().BeTrue();
            _db.Suppliers.Count().Should().Be(1);
        }

        // L1-SUP-02 | EP-Invalid | GetById id lạ -> Exception "Supplier not found" (controller map 404)
        [Fact]
        public async Task L1_SUP_02_GetById_Unknown_NotFound()
        {
            var act = () => _sut.GetByIdAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<Exception>().WithMessage("Supplier not found");
        }

        // L1-SUP-03 | EP-Invalid | Update supplier không tồn tại -> not found, không upsert
        [Fact]
        public async Task L1_SUP_03_Update_Unknown_RejectedNoUpsert()
        {
            var act = () => _sut.UpdateAsync(Guid.NewGuid(), new UpdateSupplierRequest { Name = "X", Code = "C1" });

            await act.Should().ThrowAsync<Exception>().WithMessage("Supplier not found");
            _db.Suppliers.Count().Should().Be(0);
        }
    }
}
