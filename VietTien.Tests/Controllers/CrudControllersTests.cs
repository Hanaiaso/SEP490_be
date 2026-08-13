using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Material;
using VietTien.API.DTOs.Supplier;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>
    /// Case code-driven phủ 4 controller CRUD viết cùng một khuôn:
    /// SuppliersController · VehiclesController · DiscountTiersController · MaterialController.
    /// Cả 4 trước đó đều 0%.
    ///
    /// Điểm chung cần phủ ở mỗi controller: đường thành công, và các nhánh `catch` map
    /// KeyNotFoundException -> 404 · InvalidOperationException -> 409 · Exception -> 400.
    /// Những nhánh này không chạm được qua HTTP thật nếu không dựng đúng trạng thái để
    /// service ném đúng loại exception, nên phải mock service.
    /// </summary>
    public class SuppliersControllerTests
    {
        private readonly Mock<ISupplierService> _service = new();
        private readonly SuppliersController _sut;

        public SuppliersControllerTests() => _sut = new SuppliersController(_service.Object).WithUser();

        [Fact]
        public async Task GetAll_ReturnsOkWithItems()
        {
            _service.Setup(s => s.GetAllAsync())
                .ReturnsAsync(new List<SupplierDto> { new() { Name = "NCC A" } });

            var result = await _sut.GetAll();

            result.StatusOf().Should().Be(200);
            result.Should().BeOfType<OkObjectResult>().Which.Value
                .Should().BeAssignableTo<IEnumerable<SupplierDto>>()
                .Which.Should().ContainSingle(x => x.Name == "NCC A");
        }

        [Fact]
        public async Task GetById_Found_ReturnsOk()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetByIdAsync(id)).ReturnsAsync(new SupplierDto { Id = id, Name = "NCC A" });

            (await _sut.GetById(id)).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task GetById_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Create_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateSupplierRequest>()))
                .ReturnsAsync(new SupplierDto { Name = "Moi" });

            (await _sut.Create(new CreateSupplierRequest())).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Create_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateSupplierRequest>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "trung ma")!);

            (await _sut.Create(new CreateSupplierRequest())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Update_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateSupplierRequest>()))
                .ReturnsAsync(new SupplierDto { Name = "Da sua" });

            (await _sut.Update(Guid.NewGuid(), new UpdateSupplierRequest())).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Update_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateSupplierRequest>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Update(Guid.NewGuid(), new UpdateSupplierRequest())).StatusOf().Should().Be(expected);
        }
    }

    public class VehiclesControllerTests
    {
        private readonly Mock<IVehicleService> _service = new();
        private readonly VehiclesController _sut;

        public VehiclesControllerTests()
            => _sut = new VehiclesController(_service.Object).WithUser(Guid.NewGuid(), "Admin");

        [Fact]
        public async Task GetAll_ReturnsOk()
        {
            _service.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<VehicleDto> { new() { VehicleNumber = 1 } });

            (await _sut.GetAll()).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task GetById_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Create_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateVehicleRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new VehicleDto { VehicleNumber = 9 });

            (await _sut.Create(new CreateVehicleRequest())).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Create_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateVehicleRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "trung so xe")!);

            (await _sut.Create(new CreateVehicleRequest())).StatusOf().Should().Be(expected);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task Update_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateVehicleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Update(Guid.NewGuid(), new UpdateVehicleRequest())).StatusOf().Should().Be(expected);
        }
    }

    public class DiscountTiersControllerTests
    {
        private readonly Mock<IDiscountTierService> _service = new();
        private readonly DiscountTiersController _sut;

        public DiscountTiersControllerTests()
            => _sut = new DiscountTiersController(_service.Object).WithUser(Guid.NewGuid(), "Admin");

        [Fact]
        public async Task GetAll_ReturnsOk()
        {
            _service.Setup(s => s.GetAllAsync())
                .ReturnsAsync(new List<DiscountTierDto> { new() { DiscountPercent = 0.05m } });

            (await _sut.GetAll()).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task GetById_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Create_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateDiscountTierRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new DiscountTierDto { DiscountPercent = 0.06m });

            (await _sut.Create(new CreateDiscountTierRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Create_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateDiscountTierRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("khoang chong lan"));

            (await _sut.Create(new CreateDiscountTierRequest())).StatusOf().Should().Be(400);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task Update_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateDiscountTierRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Update(Guid.NewGuid(), new UpdateDiscountTierRequest())).StatusOf().Should().Be(expected);
        }
    }

    public class MaterialControllerTests
    {
        private readonly Mock<IMaterialService> _service = new();
        private readonly Mock<IGoodsIssueService> _goodsIssueService = new();
        private readonly MaterialController _sut;

        public MaterialControllerTests()
            => _sut = new MaterialController(_service.Object, _goodsIssueService.Object).WithUser(Guid.NewGuid(), "WarehouseStaff");

        [Fact]
        public async Task GetAll_PassesSearchTermThrough()
        {
            _service.Setup(s => s.GetAllAsync("giay")).ReturnsAsync(new List<MaterialDto> { new() { Name = "Giay" } });

            (await _sut.GetAll("giay")).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetAllAsync("giay"), Times.Once, "tham số tìm kiếm phải được chuyển xuống service");
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task GetById_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Create_WhenModelStateInvalid_Returns400WithoutCallingService()
        {
            _sut.WithInvalidModelState("Name", "bat buoc");

            (await _sut.Create(new CreateMaterialDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.CreateAsync(It.IsAny<CreateMaterialDto>()), Times.Never);
        }

        [Theory]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Create_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateMaterialDto>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "trung ma")!);

            (await _sut.Create(new CreateMaterialDto())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Update_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Unit", "bat buoc");

            (await _sut.Update(Guid.NewGuid(), new UpdateMaterialDto())).StatusOf().Should().Be(400);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Update_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateMaterialDto>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Update(Guid.NewGuid(), new UpdateMaterialDto())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Delete_Success_Returns204()
        {
            _service.Setup(s => s.DeleteAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);

            (await _sut.Delete(Guid.NewGuid())).StatusOf().Should().Be(204);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(InvalidOperationException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Delete_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.DeleteAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "dang duoc dung")!);

            (await _sut.Delete(Guid.NewGuid())).StatusOf().Should().Be(expected);
        }
    }
}
