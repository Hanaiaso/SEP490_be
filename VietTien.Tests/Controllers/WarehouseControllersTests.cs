using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ WarehouseController (287 dòng, trước đó 0%).</summary>
    public class WarehouseControllerTests
    {
        private readonly Mock<IWarehouseService> _service = new();
        private readonly Mock<ICloudinaryService> _cloudinary = new();
        private readonly WarehouseController _sut;
        private readonly Guid _staffId = Guid.NewGuid();

        public WarehouseControllerTests()
            => _sut = new WarehouseController(_service.Object, _cloudinary.Object).WithUser(_staffId, "WarehouseStaff");

        private static IFormFile FakeImage(long length = 128)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.SetupGet(f => f.FileName).Returns("evidence.jpg");
            return file.Object;
        }

        // ── danh sách đơn ────────────────────────────────────────────────────

        [Fact]
        public async Task GetOrders_ClampsPagingToSafeRange()
        {
            _service.Setup(s => s.GetOrdersForWarehouseAsync("New", It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<WarehouseOrderListDto>());

            (await _sut.GetOrders("New", pageNumber: 0, pageSize: 5000)).StatusOf().Should().Be(200);

            _service.Verify(s => s.GetOrdersForWarehouseAsync("New", 1, 100), Times.Once,
                "pageNumber < 1 phải kẹp về 1 và pageSize > 100 phải kẹp về 100");
        }

        [Fact]
        public async Task GetOrders_WhenPageSizeNonPositive_FallsBackTo10()
        {
            _service.Setup(s => s.GetOrdersForWarehouseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<WarehouseOrderListDto>());

            await _sut.GetOrders("New", pageNumber: 3, pageSize: 0);

            _service.Verify(s => s.GetOrdersForWarehouseAsync("New", 3, 10), Times.Once);
        }

        [Fact]
        public async Task GetOrders_WhenTabTypeInvalid_Returns400()
        {
            _service.Setup(s => s.GetOrdersForWarehouseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new ArgumentException("tabType khong hop le"));

            (await _sut.GetOrders("linh tinh")).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetOrderDetail_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetOrderDetailAsync(It.IsAny<Guid>())).ReturnsAsync(new WarehouseOrderDetailDto());

            (await _sut.GetOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetOrderDetail_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.GetOrderDetailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay don"));

            (await _sut.GetOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetOrderDetail_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetOrderDetailAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── nhận đơn / báo thiếu hàng ────────────────────────────────────────

        [Fact]
        public async Task AcceptOrder_Success_ReturnsOkAndPassesStaffId()
        {
            (await _sut.AcceptOrder(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.AcceptOrderAsync(It.IsAny<Guid>(), _staffId), Times.Once);
        }

        [Fact]
        public async Task AcceptOrder_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.AcceptOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.AcceptOrder(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task AcceptOrder_WithoutUserClaim_Returns403()
        {
            _sut.WithAnonymousUser();

            // GetUserId() ném UnauthorizedAccessException. Từ khi action bắt riêng exception này để
            // trả 403 cho trường hợp đơn đã thuộc nhân viên khác, token không hợp lệ cũng ra 403
            // (thay vì 400 như trước) — đúng ngữ nghĩa hơn cho lỗi thiếu/không hợp lệ danh tính.
            (await _sut.AcceptOrder(Guid.NewGuid())).StatusOf().Should().Be(403);
            _service.Verify(s => s.AcceptOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task AcceptOrder_WhenOrderOwnedByAnotherStaff_Returns403()
        {
            _service.Setup(s => s.AcceptOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException("Đơn hàng này đã được nhân viên khác tiếp nhận."));

            (await _sut.AcceptOrder(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task ReportShortage_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Reason", "bat buoc");

            (await _sut.ReportShortage(Guid.NewGuid(), new ShortageAlertRequestDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.ReportShortageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShortageAlertRequestDto>()),
                Times.Never);
        }

        [Fact]
        public async Task ReportShortage_Success_ReturnsOk()
        {
            (await _sut.ReportShortage(Guid.NewGuid(), new ShortageAlertRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ReportShortage_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.ReportShortageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShortageAlertRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ReportShortage(Guid.NewGuid(), new ShortageAlertRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ReportShortage_WhenStaffOfAnotherWarehouse_Returns403()
        {
            _service.Setup(s => s.ReportShortageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShortageAlertRequestDto>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.ReportShortage(Guid.NewGuid(), new ShortageAlertRequestDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task ReportShortage_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ReportShortageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ShortageAlertRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ReportShortage(Guid.NewGuid(), new ShortageAlertRequestDto())).StatusOf().Should().Be(400);
        }

        // ── pick task ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetPickTasks_ClampsPaging()
        {
            _service.Setup(s => s.GetPickTasksAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<PickTaskDto>());

            (await _sut.GetPickTasks("InProgress", 0, 999)).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetPickTasksAsync("InProgress", 1, 100), Times.Once);
        }

        [Fact]
        public async Task GetPickTasks_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetPickTasksAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.GetPickTasks()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetPickTaskDetail_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetPickTaskDetailAsync(It.IsAny<Guid>())).ReturnsAsync(new PickTaskDto());

            (await _sut.GetPickTaskDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetPickTaskDetail_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetPickTaskDetailAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetPickTaskDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetPickTaskDetail_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetPickTaskDetailAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetPickTaskDetail(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task AcceptPickTask_Success_ReturnsOk()
        {
            (await _sut.AcceptPickTask(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AcceptPickTask_WhenMissing_Returns404()
        {
            _service.Setup(s => s.AcceptPickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.AcceptPickTask(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task AcceptPickTask_WhenAnotherStaffTookItFirst_Returns409()
        {
            _service.Setup(s => s.AcceptPickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.AcceptPickTask(Guid.NewGuid())).StatusOf().Should().Be(409,
                "hai nhân viên cùng nhận một lệnh xuất kho phải trả 409 chứ không phải 500");
        }

        [Fact]
        public async Task AcceptPickTask_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.AcceptPickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.AcceptPickTask(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WithoutImage_DoesNotCallCloudinary()
        {
            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 5, null))
                .StatusOf().Should().Be(200);

            _cloudinary.Verify(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Never);
            _service.Verify(s => s.UpdatePickTaskItemProgressAsync(
                It.IsAny<Guid>(), _staffId, It.IsAny<Guid>(), 5, null), Times.Once);
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WithImage_UploadsThenPassesUrl()
        {
            _cloudinary.Setup(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), "viettien/warehouse-evidence"))
                .ReturnsAsync("https://cdn/evidence.jpg");

            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 3, FakeImage()))
                .StatusOf().Should().Be(200);

            _service.Verify(s => s.UpdatePickTaskItemProgressAsync(
                It.IsAny<Guid>(), _staffId, It.IsAny<Guid>(), 3, "https://cdn/evidence.jpg"), Times.Once);
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WithEmptyImage_SkipsUpload()
        {
            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 1, FakeImage(length: 0)))
                .StatusOf().Should().Be(200);

            _cloudinary.Verify(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Never,
                "file rỗng thì không được tốn một lượt upload");
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WhenMissing_Returns404()
        {
            _service.Setup(s => s.UpdatePickTaskItemProgressAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 1, null)).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.UpdatePickTaskItemProgressAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 1, null)).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdatePickTaskItemProgress_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UpdatePickTaskItemProgressAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.UpdatePickTaskItemProgress(Guid.NewGuid(), Guid.NewGuid(), 1, null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CompletePickTask_Success_ReturnsOk()
        {
            (await _sut.CompletePickTask(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CompletePickTask_WhenMissing_Returns404()
        {
            _service.Setup(s => s.CompletePickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.CompletePickTask(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CompletePickTask_WhenNotTheAssignee_Returns403()
        {
            _service.Setup(s => s.CompletePickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.CompletePickTask(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task CompletePickTask_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CompletePickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.CompletePickTask(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CompletePickTask_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CompletePickTaskAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CompletePickTask(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── tập kết / bàn giao / xuất kho ────────────────────────────────────

        [Fact]
        public async Task ConsolidateOrder_Success_ReturnsOk()
        {
            (await _sut.ConsolidateOrder(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ConsolidateOrder_WhenMissing_Returns404()
        {
            _service.Setup(s => s.ConsolidateOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ConsolidateOrder(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ConsolidateOrder_WhenNotAuthorized_Returns403()
        {
            _service.Setup(s => s.ConsolidateOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.ConsolidateOrder(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task ConsolidateOrder_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ConsolidateOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ConsolidateOrder(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task HandoverOrder_WhenSignaturesAreBase64_UploadsBothAndReplacesWithUrls()
        {
            _cloudinary.Setup(c => c.UploadBase64ImageAsync(It.IsAny<string>(), "viettien/handovers", It.IsAny<string>()))
                .ReturnsAsync("https://cdn/sig.png");
            _service.Setup(s => s.HandoverOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<HandoverRequestDto>()))
                .ReturnsAsync(new HandoverResultDto { Message = "ok", IsConfirmed = true, GoodsIssueId = Guid.NewGuid(), GoodsIssueCode = "GI-1" });
            var request = new HandoverRequestDto
            {
                WarehouseSignature = "data:image/png;base64,AAA",
                SalesSignature = "data:image/png;base64,BBB"
            };

            (await _sut.HandoverOrder(Guid.NewGuid(), request)).StatusOf().Should().Be(200);

            request.WarehouseSignature.Should().Be("https://cdn/sig.png");
            request.SalesSignature.Should().Be("https://cdn/sig.png");
            _cloudinary.Verify(c => c.UploadBase64ImageAsync(It.IsAny<string>(), "viettien/handovers", It.IsAny<string>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task HandoverOrder_WhenSignaturesAlreadyUrls_SkipsUpload()
        {
            _service.Setup(s => s.HandoverOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<HandoverRequestDto>()))
                .ReturnsAsync(new HandoverResultDto { Message = "Đã ghi nhận chữ ký, chờ bên còn lại xác nhận.", IsConfirmed = false });
            var request = new HandoverRequestDto
            {
                WarehouseSignature = "https://cdn/da-upload.png",
                SalesSignature = null
            };

            (await _sut.HandoverOrder(Guid.NewGuid(), request)).StatusOf().Should().Be(200);

            _cloudinary.Verify(c => c.UploadBase64ImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never, "chữ ký đã là URL thì không upload lại");
        }

        [Fact]
        public async Task HandoverOrder_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.HandoverOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<HandoverRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.HandoverOrder(Guid.NewGuid(), new HandoverRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task HandoverOrder_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.HandoverOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<HandoverRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.HandoverOrder(Guid.NewGuid(), new HandoverRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task PostGoodsIssue_Success_ReturnsOk()
        {
            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), _staffId), Times.Once);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenMissing_Returns404()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenStockInsufficient_Returns400()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Ton kho khong du"));

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ GoodsIssueController (186 dòng, trước đó 0%).</summary>
    public class GoodsIssueControllerTests
    {
        private readonly Mock<IGoodsIssueService> _service = new();
        private readonly GoodsIssueController _sut;
        private readonly Guid _staffId = Guid.NewGuid();

        public GoodsIssueControllerTests()
            => _sut = new GoodsIssueController(_service.Object).WithUser(_staffId, "WarehouseStaff");

        private static IFormFile FakeFile(long length = 64)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.SetupGet(f => f.FileName).Returns("proof.pdf");
            return file.Object;
        }

        [Fact]
        public async Task GetGoodsIssues_PassesTypeFilter()
        {
            _service.Setup(s => s.GetGoodsIssuesAsync("Reversal", _staffId)).ReturnsAsync(new List<GoodsIssueDto>());

            (await _sut.GetGoodsIssues("Reversal")).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetGoodsIssuesAsync("Reversal", _staffId), Times.Once);
        }

        [Fact]
        public async Task GetGoodsIssues_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetGoodsIssuesAsync(It.IsAny<string?>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetGoodsIssues(null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetGoodsIssueById_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetGoodsIssueByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(new GoodsIssueDto());

            (await _sut.GetGoodsIssueById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetGoodsIssueById_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetGoodsIssueByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetGoodsIssueById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetGoodsIssueById_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetGoodsIssueByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetGoodsIssueById(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateGoodsIssue_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.CreateGoodsIssue(new CreateGoodsIssueRequestDto())).StatusOf().Should().Be(401);
            _service.Verify(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateGoodsIssue_Success_ReturnsOkWithStaffIdFromToken()
        {
            _service.Setup(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), _staffId))
                .ReturnsAsync(new GoodsIssueDto());

            (await _sut.CreateGoodsIssue(new CreateGoodsIssueRequestDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), _staffId), Times.Once);
        }

        [Fact]
        public async Task CreateGoodsIssue_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.CreateGoodsIssue(new CreateGoodsIssueRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateGoodsIssue_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CreateGoodsIssue(new CreateGoodsIssueRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadProof_WhenNoFile_Returns400WithoutCallingService()
        {
            (await _sut.UploadProof(Guid.NewGuid(), null!)).StatusOf().Should().Be(400);
            _service.Verify(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()), Times.Never);
        }

        [Fact]
        public async Task UploadProof_WhenFileEmpty_Returns400()
        {
            (await _sut.UploadProof(Guid.NewGuid(), FakeFile(length: 0))).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadProof_Success_ReturnsOk()
        {
            _service.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ReturnsAsync(new GoodsIssueDto());

            (await _sut.UploadProof(Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UploadProof_WhenIssueMissing_Returns404()
        {
            _service.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.UploadProof(Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UploadProof_WhenIssueAlreadyPosted_Returns409()
        {
            _service.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new InvalidOperationException("Phieu da phat hanh"));

            (await _sut.UploadProof(Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UploadProof_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.UploadProof(Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UploadProof_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.UploadProof(Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateHandoverInfo_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdateHandoverInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateGoodsIssueHandoverDto>()))
                .ReturnsAsync(new GoodsIssueDto());

            (await _sut.UpdateHandoverInfo(Guid.NewGuid(), new UpdateGoodsIssueHandoverDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateHandoverInfo_WhenMissing_Returns404()
        {
            _service.Setup(s => s.UpdateHandoverInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateGoodsIssueHandoverDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.UpdateHandoverInfo(Guid.NewGuid(), new UpdateGoodsIssueHandoverDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateHandoverInfo_WhenWrongState_Returns409()
        {
            _service.Setup(s => s.UpdateHandoverInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateGoodsIssueHandoverDto>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await _sut.UpdateHandoverInfo(Guid.NewGuid(), new UpdateGoodsIssueHandoverDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdateHandoverInfo_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.UpdateHandoverInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateGoodsIssueHandoverDto>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.UpdateHandoverInfo(Guid.NewGuid(), new UpdateGoodsIssueHandoverDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdateHandoverInfo_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UpdateHandoverInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateGoodsIssueHandoverDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.UpdateHandoverInfo(Guid.NewGuid(), new UpdateGoodsIssueHandoverDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task PostGoodsIssue_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task PostGoodsIssue_Success_ReturnsOk()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), _staffId)).ReturnsAsync(new GoodsIssueDto());

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenMissing_Returns404()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenStockInsufficient_Returns409()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Ton kho khong du"));

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PostGoodsIssue_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.PostGoodsIssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateReversal_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task CreateReversal_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateReversalAsync(It.IsAny<Guid>(), It.IsAny<CreateReversalRequestDto>(), _staffId))
                .ReturnsAsync(new GoodsIssueDto());

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateReversal_WhenMissing_Returns404()
        {
            _service.Setup(s => s.CreateReversalAsync(It.IsAny<Guid>(), It.IsAny<CreateReversalRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateReversal_WhenIssueNotPosted_Returns409()
        {
            _service.Setup(s => s.CreateReversalAsync(It.IsAny<Guid>(), It.IsAny<CreateReversalRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Phieu chua phat hanh, khong the dao"));

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateReversal_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.CreateReversalAsync(It.IsAny<Guid>(), It.IsAny<CreateReversalRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateReversal_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateReversalAsync(It.IsAny<Guid>(), It.IsAny<CreateReversalRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CreateReversal(Guid.NewGuid(), new CreateReversalRequestDto())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>
    /// Case code-driven phủ WarehouseManagementController (302 dòng, trước đó 0%).
    /// Nửa CRUD dùng mock service; nửa Quarantine controller truy vấn thẳng
    /// ApplicationDbContext nên phải chạy trên EF InMemory.
    /// </summary>
    public class WarehouseManagementControllerTests
    {
        private readonly Mock<IWarehouseManagementService> _service = new();
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly WarehouseManagementController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public WarehouseManagementControllerTests()
            => _sut = new WarehouseManagementController(
                    _service.Object, _db, TestWarehouseAccessGuard.Create(_db), new NoOpAuditLogService())
                .WithUser(_userId, "WarehouseStaff");

        // ── CRUD kho ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetAll_ReturnsOk()
        {
            _service.Setup(s => s.GetAllWarehousesAsync()).ReturnsAsync(new List<WarehouseDto>());

            (await _sut.GetAll()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetAll_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetAllWarehousesAsync()).ThrowsAsync(new Exception("loi"));

            (await _sut.GetAll()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetById_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetWarehouseByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new WarehouseDto());

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetById_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetWarehouseByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay kho"));

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Create_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Name", "bat buoc");

            (await _sut.Create(new CreateWarehouseDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.CreateWarehouseAsync(It.IsAny<CreateWarehouseDto>()), Times.Never);
        }

        [Fact]
        public async Task Create_Success_Returns201()
        {
            _service.Setup(s => s.CreateWarehouseAsync(It.IsAny<CreateWarehouseDto>()))
                .ReturnsAsync(new WarehouseDto { Id = Guid.NewGuid() });

            (await _sut.Create(new CreateWarehouseDto())).StatusOf().Should().Be(201);
        }

        [Fact]
        public async Task Create_WhenCodeDuplicated_Returns400()
        {
            _service.Setup(s => s.CreateWarehouseAsync(It.IsAny<CreateWarehouseDto>()))
                .ThrowsAsync(new InvalidOperationException("Ma kho da ton tai"));

            (await _sut.Create(new CreateWarehouseDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Update_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Name", "bat buoc");

            (await _sut.Update(Guid.NewGuid(), new UpdateWarehouseDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Update_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdateWarehouseAsync(It.IsAny<Guid>(), It.IsAny<UpdateWarehouseDto>()))
                .ReturnsAsync(new WarehouseDto());

            (await _sut.Update(Guid.NewGuid(), new UpdateWarehouseDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Update_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UpdateWarehouseAsync(It.IsAny<Guid>(), It.IsAny<UpdateWarehouseDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Update(Guid.NewGuid(), new UpdateWarehouseDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Delete_Success_ReturnsOk()
        {
            (await _sut.Delete(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.DeleteWarehouseAsync(It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task Delete_WhenWarehouseStillHasStock_Returns400()
        {
            _service.Setup(s => s.DeleteWarehouseAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Kho con ton hang"));

            (await _sut.Delete(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── quarantine ───────────────────────────────────────────────────────

        private async Task<(Inventory inv, Order order, User receiver)> SeedForQuarantineAsync()
        {
            var receiver = new User { Id = _userId, FullName = "Thu kho", Email = "tk@vt.vn", PasswordHash = "x" };
            var product = new Product { Name = "Màng bọc", Sku = "SKU-1", StandardListedPrice = 1000 };
            var inv = new Inventory { Product = product, OnHandQuantity = 100, QuarantineQuantity = 0 };
            var order = new Order { OrderCode = "ORD-001" };

            _db.Users.Add(receiver);
            _db.Products.Add(product);
            _db.Inventories.Add(inv);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();
            return (inv, order, receiver);
        }

        [Fact]
        public async Task GetQuarantineList_MapsProductNameAndStatus()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            _db.QuarantineLogs.Add(new QuarantineLog
            {
                QuarantineCode = "QZ-001",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 5,
                Reason = "hang loi",
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            });
            await _db.SaveChangesAsync();

            var result = await _sut.GetQuarantineList();

            result.StatusOf().Should().Be(200);
            var item = ((result.Result as ObjectResult)!.Value as List<QuarantineListItemDto>)!.Single();
            item.ItemName.Should().Be("Màng bọc");
            item.ItemType.Should().Be("Product");
            item.OrderCode.Should().Be("ORD-001");
            item.DispatchedAction.Should().BeNull("bản ghi còn Waiting thì chưa có hành động xử lý");
        }

        [Fact]
        public async Task ReceiveToQuarantine_WhenNoInventoryForProduct_Returns404()
        {
            var result = await _sut.ReceiveToQuarantine(new QuarantineReceiveDto
            {
                ProductId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                Quantity = 1
            });

            result.StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ReceiveToQuarantine_WhenOrderMissing_Returns404()
        {
            var (inv, _, _) = await SeedForQuarantineAsync();

            var result = await _sut.ReceiveToQuarantine(new QuarantineReceiveDto
            {
                ProductId = inv.ProductId!.Value,
                OrderId = Guid.NewGuid(),
                Quantity = 1
            });

            result.StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ReceiveToQuarantine_Success_IncreasesQuarantineQtyAndWritesLog()
        {
            var (inv, order, _) = await SeedForQuarantineAsync();

            var result = await _sut.ReceiveToQuarantine(new QuarantineReceiveDto
            {
                ProductId = inv.ProductId!.Value,
                OrderId = order.Id,
                Quantity = 7,
                Reason = "khach tra hang loi"
            });

            result.StatusOf().Should().Be(200);
            inv.QuarantineQuantity.Should().Be(7);
            var log = _db.QuarantineLogs.Single();
            log.Status.Should().Be(QuarantineStatus.Waiting);
            log.ReceivedByUserId.Should().Be(_userId, "phải ghi nhận đúng người nhập cách ly");
            log.InventoryId.Should().Be(inv.Id);
        }

        [Fact]
        public async Task ReceiveToQuarantine_WithoutUserClaim_Returns403()
        {
            _sut.WithAnonymousUser();

            // Action nay bắt riêng UnauthorizedAccessException để trả 403 khi thao tác ngoài kho
            // được phân công; token thiếu claim cũng đi vào nhánh đó thay vì rơi xuống 400.
            (await _sut.ReceiveToQuarantine(new QuarantineReceiveDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task DispatchQuarantine_WhenLogMissing_Returns404()
        {
            (await _sut.DispatchQuarantine(Guid.NewGuid(), new QuarantineDispatchDto { Action = "available" }))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task DispatchQuarantine_WhenAlreadyProcessed_Returns400()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-002",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 3,
                Status = QuarantineStatus.ApprovedAvailable,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "available" }))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task DispatchQuarantine_Available_ReleasesBackToStock()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            inv.QuarantineQuantity = 10;
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-003",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 4,
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "available", Notes = "dat" }))
                .StatusOf().Should().Be(200);

            inv.QuarantineQuantity.Should().Be(6);
            inv.DamagedQuantity.Should().Be(0);
            log.Status.Should().Be(QuarantineStatus.ApprovedAvailable);
            log.DispatchedByUserId.Should().Be(_userId);
            log.DispatchedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task DispatchQuarantine_Damaged_MovesQtyToDamagedBucket()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            inv.QuarantineQuantity = 10;
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-004",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 4,
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "DAMAGED" }))
                .StatusOf().Should().Be(200);

            inv.QuarantineQuantity.Should().Be(6);
            inv.DamagedQuantity.Should().Be(4, "hàng hỏng phải chuyển sang bucket Damaged, không bốc hơi");
            log.Status.Should().Be(QuarantineStatus.ApprovedDamaged);
        }

        [Fact]
        public async Task DispatchQuarantine_WithUnknownAction_Returns400()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-005",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = inv.Id,
                Quantity = 1,
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "xoa-luon" }))
                .StatusOf().Should().Be(400);
            log.Status.Should().Be(QuarantineStatus.Waiting, "action sai thì không được đổi trạng thái");
        }

        [Fact]
        public async Task DispatchQuarantine_LegacyLogWithoutInventoryLink_SelfHealsThenReleases()
        {
            var (inv, order, receiver) = await SeedForQuarantineAsync();
            // Dữ liệu cũ: log không trỏ tới Inventory và lúc nhập chưa cộng QuarantineQuantity.
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-006",
                OrderId = order.Id,
                ProductId = inv.ProductId,
                InventoryId = null,
                Quantity = 3,
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "available" }))
                .StatusOf().Should().Be(200);

            log.InventoryId.Should().Be(inv.Id, "nhánh auto-heal phải gắn lại liên kết tồn kho");
            inv.QuarantineQuantity.Should().Be(0, "cộng bù 3 rồi trừ 3 thì về 0, không được âm");
        }

        [Fact]
        public async Task DispatchQuarantine_LegacyLogWithNoMatchingInventory_Returns400()
        {
            var (_, order, receiver) = await SeedForQuarantineAsync();
            var log = new QuarantineLog
            {
                QuarantineCode = "QZ-007",
                OrderId = order.Id,
                ProductId = Guid.NewGuid(),   // sản phẩm không có dòng tồn kho nào
                InventoryId = null,
                Quantity = 1,
                Status = QuarantineStatus.Waiting,
                ReceivedByUserId = receiver.Id
            };
            _db.QuarantineLogs.Add(log);
            await _db.SaveChangesAsync();

            (await _sut.DispatchQuarantine(log.Id, new QuarantineDispatchDto { Action = "available" }))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task DispatchQuarantine_WithoutUserClaim_Returns403()
        {
            _sut.WithAnonymousUser();

            (await _sut.DispatchQuarantine(Guid.NewGuid(), new QuarantineDispatchDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetStaff_ReturnsOnlyWarehouseRoles()
        {
            _db.Users.AddRange(
                new User { FullName = "Thu kho", Email = "a@vt.vn", PasswordHash = "x", Role = SystemRole.WarehouseStaff },
                new User { FullName = "Sếp", Email = "b@vt.vn", PasswordHash = "x", Role = SystemRole.CEO },
                new User { FullName = "Khach", Email = "c@vt.vn", PasswordHash = "x", Role = SystemRole.Customer });
            await _db.SaveChangesAsync();

            var result = await _sut.GetStaff(_db);

            result.StatusOf().Should().Be(200);
            var body = (result as ObjectResult)!.Value!;
            body.Should().BeAssignableTo<IEnumerable<object>>().Which.Should().HaveCount(2,
                "chỉ WarehouseStaff/CEO/Admin mới được liệt kê, không lộ tài khoản khách");
        }
    }
}
