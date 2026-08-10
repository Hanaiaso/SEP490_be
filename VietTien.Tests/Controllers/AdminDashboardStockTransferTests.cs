using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ AdminSystemConfigController (68 dòng, trước đó 42,1%).</summary>
    public class AdminSystemConfigControllerTests
    {
        private readonly Mock<ISystemConfigService> _service = new();
        private readonly AdminSystemConfigController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public AdminSystemConfigControllerTests()
            => _sut = new AdminSystemConfigController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task GetAll_ReturnsOk()
        {
            _service.Setup(s => s.GetAllWithEffectiveValuesAsync()).ReturnsAsync(new List<SystemConfigDto>());

            (await _sut.GetAll()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetHistory_Success_ForwardsKey()
        {
            _service.Setup(s => s.GetHistoryAsync("SePayThresholdMinutes"))
                .ReturnsAsync(new List<SystemConfigVersionDto>());

            (await _sut.GetHistory("SePayThresholdMinutes")).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetHistoryAsync("SePayThresholdMinutes"), Times.Once);
        }

        [Fact]
        public async Task GetHistory_WhenKeyUnknown_Returns404()
        {
            _service.Setup(s => s.GetHistoryAsync(It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("khong co key nay"));

            (await _sut.GetHistory("KhongTonTai")).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetHistory_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetHistoryAsync(It.IsAny<string>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetHistory("x")).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Update_Success_PassesActorIdentityForAuditTrail()
        {
            _service.Setup(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new SystemConfigDto());

            (await _sut.Update("SePayThresholdMinutes", new UpdateSystemConfigRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.SetValueAsync("SePayThresholdMinutes", It.IsAny<UpdateSystemConfigRequest>(),
                _adminId, It.IsAny<string>(), It.IsAny<string?>()), Times.Once,
                "đổi cấu hình hệ thống phải ghi lại ai đổi để truy vết");
        }

        [Fact]
        public async Task Update_WhenKeyUnknown_Returns404()
        {
            _service.Setup(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Update("x", new UpdateSystemConfigRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Update_WhenValueOutOfRange_Returns400()
        {
            _service.Setup(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new ArgumentException("Gia tri ngoai khoang cho phep"));

            (await _sut.Update("x", new UpdateSystemConfigRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Update_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            (await _sut.Update("x", new UpdateSystemConfigRequest())).StatusOf().Should().Be(400);
            _service.Verify(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }
    }

    /// <summary>Case code-driven phủ AuditLogController (32 dòng, trước đó 69,2%).</summary>
    public class AuditLogControllerTests
    {
        private readonly Mock<IAuditLogService> _service = new();
        private readonly AuditLogController _sut;

        public AuditLogControllerTests()
            => _sut = new AuditLogController(_service.Object).WithUser(Guid.NewGuid(), "Admin");

        [Fact]
        public async Task Search_ForwardsQuery()
        {
            var query = new AuditLogQueryDto();
            _service.Setup(s => s.SearchAsync(query)).ReturnsAsync(new PagedResultDto<AuditLogDto>());

            (await _sut.Search(query)).StatusOf().Should().Be(200);
            _service.Verify(s => s.SearchAsync(query), Times.Once);
        }

        [Fact]
        public async Task Export_ReturnsCsvFileWithTimestampedName()
        {
            _service.Setup(s => s.ExportCsvAsync(It.IsAny<AuditLogQueryDto>()))
                .ReturnsAsync(new byte[] { 1, 2, 3 });

            var result = await _sut.Export(new AuditLogQueryDto());

            var file = result.Should().BeOfType<FileContentResult>().Subject;
            file.ContentType.Should().Be("text/csv");
            file.FileDownloadName.Should().StartWith("audit-logs-").And.EndWith(".csv");
            file.FileContents.Should().HaveCount(3);
        }
    }

    /// <summary>
    /// Case code-driven phủ DashboardsController (87 dòng, trước đó 65,3%).
    /// Trọng tâm: hàm private ResolveRange (mặc định 30 ngày, chặn khoảng ngày ngược).
    /// </summary>
    public class DashboardsControllerTests
    {
        private readonly Mock<ISalesStaffDashboardService> _staff = new();
        private readonly Mock<ISalesManagerDashboardService> _manager = new();
        private readonly Mock<ICeoDashboardService> _ceo = new();
        private readonly Mock<IWarehouseDashboardService> _warehouse = new();
        private readonly Mock<IAdminDashboardService> _admin = new();
        private readonly Guid _userId = Guid.NewGuid();

        private DashboardsController Build(string role)
            => new DashboardsController(_staff.Object, _manager.Object, _ceo.Object, _warehouse.Object, _admin.Object).WithUser(_userId, role);

        [Fact]
        public async Task GetSalesStaffDashboard_WhenNoRange_DefaultsToLast30Days()
        {
            DateTime capturedFrom = default, capturedTo = default;
            _staff.Setup(s => s.GetDashboardAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .Callback<Guid, DateTime, DateTime>((_, f, t) => { capturedFrom = f; capturedTo = t; })
                .ReturnsAsync(new SalesStaffDashboardDto());

            (await Build("SalesStaff").GetSalesStaffDashboard(null, null)).StatusOf().Should().Be(200);

            (capturedTo - capturedFrom).TotalDays.Should().BeApproximately(30, 0.01,
                "không truyền khoảng ngày thì mặc định 30 ngày gần nhất");
        }

        [Fact]
        public async Task GetSalesStaffDashboard_ScopesToCallerId()
        {
            _staff.Setup(s => s.GetDashboardAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new SalesStaffDashboardDto());

            await Build("SalesStaff").GetSalesStaffDashboard(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            _staff.Verify(s => s.GetDashboardAsync(_userId, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)),
                Times.Once, "SalesStaff chỉ xem số liệu của chính mình");
        }

        [Fact]
        public async Task GetSalesStaffDashboard_WhenRangeReversed_Returns400()
        {
            var result = await Build("SalesStaff")
                .GetSalesStaffDashboard(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));

            result.StatusOf().Should().Be(400);
            _staff.Verify(s => s.GetDashboardAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()),
                Times.Never);
        }

        [Fact]
        public async Task GetSalesManagerDashboard_Success_NoUserScoping()
        {
            _manager.Setup(s => s.GetDashboardAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new SalesManagerDashboardDto());

            (await Build("SalesManager").GetSalesManagerDashboard(null, null)).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetSalesManagerDashboard_WhenRangeReversed_Returns400()
        {
            (await Build("SalesManager")
                .GetSalesManagerDashboard(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1)))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetCeoDashboard_Success_ReturnsOk()
        {
            _ceo.Setup(s => s.GetDashboardAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new CeoDashboardDto());

            (await Build("CEO").GetCeoDashboard(null, null)).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetCeoDashboard_WhenRangeReversed_Returns400()
        {
            (await Build("CEO").GetCeoDashboard(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1)))
                .StatusOf().Should().Be(400);
        }
    }

    /// <summary>
    /// Bổ sung cho StockTransferController (68,3%): endpoint `receive` và các nhánh lỗi
    /// còn thiếu của `request-transport` / `dispatch` / `cancel`.
    /// </summary>
    public class StockTransferControllerReceiveTests
    {
        private readonly Mock<IStockTransferService> _service = new();
        private readonly StockTransferController _sut;
        private readonly Guid _staffId = Guid.NewGuid();

        public StockTransferControllerReceiveTests()
            => _sut = new StockTransferController(_service.Object).WithUser(_staffId, "WarehouseStaff");

        [Fact]
        public async Task ReceiveTransfer_Success_PassesReceiverIdFromToken()
        {
            _service.Setup(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), _staffId))
                .ReturnsAsync(new StockTransferDto());

            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), _staffId),
                Times.Once);
        }

        [Fact]
        public async Task ReceiveTransfer_WhenTransferMissing_Returns404()
        {
            _service.Setup(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ReceiveTransfer_WhenStaffOfAnotherWarehouse_Returns403()
        {
            _service.Setup(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(403,
                "chỉ thủ kho của kho đích mới được nhận hàng điều chuyển");
        }

        [Fact]
        public async Task ReceiveTransfer_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ReceiveTransfer_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ReceiveAsync(It.IsAny<Guid>(), It.IsAny<ReceiveStockTransferDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ReceiveTransfer_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            // GetUserId() ném UnauthorizedAccessException -> rơi vào catch riêng -> Forbid().
            (await _sut.ReceiveTransfer(Guid.NewGuid(), new ReceiveStockTransferDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task RequestTransport_WhenTransferMissing_Returns404()
        {
            _service.Setup(s => s.RequestTransportAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.RequestTransport(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestTransport_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.RequestTransportAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.RequestTransport(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task RequestTransport_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.RequestTransportAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.RequestTransport(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task DispatchTransfer_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.DispatchAsync(It.IsAny<Guid>())).ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.DispatchTransfer(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CancelTransfer_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CancelAsync(It.IsAny<Guid>())).ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.CancelTransfer(Guid.NewGuid())).StatusOf().Should().Be(409);
        }
    }
}
