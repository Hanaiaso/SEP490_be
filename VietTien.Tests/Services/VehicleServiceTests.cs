using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: VehicleService — L1-VEH-01..04. EF InMemory + mock IAuditLogService.
    /// ⚠ L1-VEH-05 (đưa xe đang có chuyến trong tương lai vào bảo trì -> chặn) BỊ BLOCKED ở tầng L1:
    ///    VehicleService không hề biết tới lịch chuyến (không có quan hệ với Order/DeliverySchedule),
    ///    nên ràng buộc này chưa có chỗ để tồn tại. Xem DOC_MISMATCHES.md.
    /// ⚠ GetAllAsync() KHÔNG nhận query filter — test khẳng định DTO trả về đủ cờ IsActive để
    ///    tầng gọi lọc, thay vì truyền filter vào service.
    /// </summary>
    public class VehicleServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly VehicleService _sut;

        private static readonly Guid ActorId = Guid.NewGuid();
        private const string ActorEmail = "admin@viettien.com";
        private const string ActorIp = "127.0.0.1";

        public VehicleServiceTests()
        {
            _sut = new VehicleService(_db, _audit.Object);
        }

        // L1-VEH-01 | EP-Valid | 5 xe (4 hoạt động, 1 bảo trì) -> chỉ 4 xe active dùng để xếp lịch
        [Fact]
        public async Task L1_VEH_01_GetAll_MarksMaintenanceVehiclesAsInactive()
        {
            _db.Vehicles.AddRange(
                TestData.Vehicle(1), TestData.Vehicle(2), TestData.Vehicle(3), TestData.Vehicle(4),
                TestData.Vehicle(5, v => v.IsActive = false)); // đang bảo trì
            _db.SaveChanges();

            var all = await _sut.GetAllAsync();

            all.Should().HaveCount(5);
            all.Count(v => v.IsActive).Should().Be(4, "chỉ 4 xe khả dụng để chọn lịch");
            all.Single(v => !v.IsActive).VehicleNumber.Should().Be(5);
        }

        // L1-VEH-02 | EP-Valid | Tạo xe mới -> lưu thành công, mặc định trạng thái hoạt động
        [Fact]
        public async Task L1_VEH_02_Create_DefaultsToActive()
        {
            var dto = await _sut.CreateAsync(
                new CreateVehicleRequest { VehicleNumber = 1, LicensePlate = "51C-12345", Capacity = 2000m },
                ActorId, ActorEmail, ActorIp);

            dto.IsActive.Should().BeTrue();
            dto.LicensePlate.Should().Be("51C-12345");
            _db.Vehicles.Should().ContainSingle();
        }

        // L1-VEH-03 | EP-Invalid | Tạo xe TRÙNG BIỂN SỐ -> từ chối, không tạo bản ghi thứ 2
        // 🔴 SPEC GAP v2.2: CreateAsync chỉ kiểm tra trùng VehicleNumber, KHÔNG kiểm tra trùng
        // LicensePlate. Test ĐỎ cho tới khi bổ sung ràng buộc duy nhất trên biển số (BR-037).
        [Fact]
        public async Task L1_VEH_03_Create_DuplicateLicensePlate_IsRejected()
        {
            _db.Vehicles.Add(TestData.Vehicle(1, v => v.LicensePlate = "51C-12345"));
            _db.SaveChanges();

            var act = () => _sut.CreateAsync(
                new CreateVehicleRequest { VehicleNumber = 2, LicensePlate = "51C-12345" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("biển số xe phải là duy nhất");
            _db.Vehicles.Count(v => v.LicensePlate == "51C-12345").Should().Be(1);
        }

        // L1-VEH-04 | EP-Valid | Chuyển xe sang bảo trì -> không còn nằm trong danh sách xe khả dụng
        [Fact]
        public async Task L1_VEH_04_SetMaintenance_RemovesFromAvailableFleet()
        {
            var vehicle = TestData.Vehicle(1);
            _db.Vehicles.Add(vehicle);
            _db.SaveChanges();

            var after = await _sut.UpdateAsync(vehicle.Id,
                new UpdateVehicleRequest { LicensePlate = vehicle.LicensePlate, IsActive = false, Note = "Bảo dưỡng định kỳ" },
                ActorId, ActorEmail, ActorIp);

            after.IsActive.Should().BeFalse();
            (await _sut.GetAllAsync()).Where(v => v.IsActive).Should().BeEmpty("không còn xe nào để lập chuyến");
            _audit.Verify(a => a.LogAsync(
                "Vehicle", vehicle.Id.ToString(), "UPDATE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), ActorIp), Times.Once);
        }

        // L1-VEH-05 | EP-Invalid | Đưa xe đang có chuyến trong tương lai vào bảo trì -> chặn.
        // ĐÃ SỬA: VehicleService giờ đối chiếu Order.DeliveryVehicleId theo VehicleNumber.
        [Fact]
        public async Task L1_VEH_05_SetMaintenance_WithUpcomingDelivery_Blocked()
        {
            var vehicle = TestData.Vehicle(1);
            _db.Vehicles.Add(vehicle);
            var order = TestData.Order(Guid.NewGuid(), o =>
            {
                o.DeliveryVehicleId = vehicle.VehicleNumber;
                o.DeliveryStatus = VietTien.API.Models.DeliveryStatus.Scheduled;
                o.ScheduledDeliveryDate = DateTime.UtcNow.Date.AddDays(1);
            });
            _db.Orders.Add(order);
            _db.SaveChanges();

            var act = () => _sut.UpdateAsync(vehicle.Id,
                new UpdateVehicleRequest { LicensePlate = vehicle.LicensePlate, IsActive = false, Note = "Bảo dưỡng" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await _sut.GetByIdAsync(vehicle.Id)).IsActive.Should().BeTrue("chưa xử lý xong chuyến đã xếp thì chưa được khoá xe");
        }
    }
}
