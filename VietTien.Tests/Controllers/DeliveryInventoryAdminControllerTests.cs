using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Marketing;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ DeliveryController (228 dòng, trước đó 3%).</summary>
    public class DeliveryControllerTests
    {
        private readonly Mock<IOrderService> _service = new();
        private readonly Mock<IDeliveryTripService> _tripService = new();
        private readonly DeliveryController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public DeliveryControllerTests()
            => _sut = new DeliveryController(_service.Object, _tripService.Object).WithUser(_userId, "SalesStaff");

        // ── bước 1: lập lịch xe ──────────────────────────────────────────────

        [Fact]
        public async Task ScheduleDelivery_Success_ReturnsOkAndPassesCallerId()
        {
            _service.Setup(s => s.ScheduleDeliveryAsync(_userId, It.IsAny<ScheduleDeliveryRequestDto>()))
                .ReturnsAsync(new ScheduleDeliveryResponseDto());

            (await _sut.ScheduleDelivery(new ScheduleDeliveryRequestDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.ScheduleDeliveryAsync(_userId, It.IsAny<ScheduleDeliveryRequestDto>()), Times.Once);
        }

        [Fact]
        public async Task ScheduleDelivery_WhenVehicleAlreadyBooked_Returns409()
        {
            _service.Setup(s => s.ScheduleDeliveryAsync(It.IsAny<Guid>(), It.IsAny<ScheduleDeliveryRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Xe đã có lịch trong ca này"));

            (await _sut.ScheduleDelivery(new ScheduleDeliveryRequestDto())).StatusOf().Should().Be(409,
                "xung đột lịch xe phải là 409 để FE phân biệt với lỗi dữ liệu");
        }

        [Fact]
        public async Task ScheduleDelivery_WhenOrderMissing_Returns400()
        {
            _service.Setup(s => s.ScheduleDeliveryAsync(It.IsAny<Guid>(), It.IsAny<ScheduleDeliveryRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay don"));

            // Action này KHÔNG có catch KeyNotFoundException riêng -> rơi vào catch(Exception) = 400.
            (await _sut.ScheduleDelivery(new ScheduleDeliveryRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ScheduleDelivery_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            (await _sut.ScheduleDelivery(new ScheduleDeliveryRequestDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.ScheduleDeliveryAsync(It.IsAny<Guid>(), It.IsAny<ScheduleDeliveryRequestDto>()),
                Times.Never);
        }

        [Fact]
        public async Task GetDeliveryOrders_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetDeliveryOrdersAsync(_userId)).ReturnsAsync(new List<DeliveryOrderListDto>());

            (await _sut.GetDeliveryOrders()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetDeliveryOrdersAsync(_userId), Times.Once,
                "chỉ được trả đơn giao của Sale đang đăng nhập");
        }

        [Fact]
        public async Task GetDeliveryOrders_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetDeliveryOrdersAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetDeliveryOrders()).StatusOf().Should().Be(400);
        }

        // ── bước 2: ghi nhận kết quả giao (POD + COD) ────────────────────────

        [Fact]
        public async Task CompleteDelivery_Success_ReturnsOk()
        {
            _service.Setup(s => s.RecordDeliveryResultAsync(It.IsAny<Guid>(), _userId, It.IsAny<RecordDeliveryResultDto>()))
                .ReturnsAsync(new DeliveryResultResponseDto());

            (await _sut.CompleteDelivery(Guid.NewGuid(), new RecordDeliveryResultDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CompleteDelivery_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.RecordDeliveryResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RecordDeliveryResultDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay don"));

            (await _sut.CompleteDelivery(Guid.NewGuid(), new RecordDeliveryResultDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CompleteDelivery_WhenOrderNotInDelivery_Returns400()
        {
            _service.Setup(s => s.RecordDeliveryResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RecordDeliveryResultDto>()))
                .ThrowsAsync(new InvalidOperationException("Don chua o trang thai dang giao"));

            (await _sut.CompleteDelivery(Guid.NewGuid(), new RecordDeliveryResultDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CompleteDelivery_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.RecordDeliveryResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RecordDeliveryResultDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CompleteDelivery(Guid.NewGuid(), new RecordDeliveryResultDto())).StatusOf().Should().Be(400);
        }

        // ── bước 3: yêu cầu hủy đơn đã thanh toán (CR-06) ────────────────────

        [Fact]
        public async Task RequestCancelPaidOrder_Success_ReturnsOkAndForwardsReason()
        {
            (await _sut.RequestCancelPaidOrder(Guid.NewGuid(), new CancelPaidOrderRequestDto { Reason = "khach doi y" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.RequestCancelPaidOrderAsync(It.IsAny<Guid>(), _userId, "khach doi y"), Times.Once);
        }

        [Fact]
        public async Task RequestCancelPaidOrder_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.RequestCancelPaidOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.RequestCancelPaidOrder(Guid.NewGuid(), new CancelPaidOrderRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestCancelPaidOrder_WhenOrderNotPaid_PropagatesToMiddlewareAs409()
        {
            // GH-14: controller không còn tự bắt InvalidOperationException — để
            // ExceptionHandlingMiddleware map đúng 409 Conflict (trước đây bắt cục bộ trả nhầm 400).
            _service.Setup(s => s.RequestCancelPaidOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Don chua thanh toan"));

            var act = () => _sut.RequestCancelPaidOrder(Guid.NewGuid(), new CancelPaidOrderRequestDto());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task RequestCancelPaidOrder_WhenServiceThrows_PropagatesToMiddleware()
        {
            _service.Setup(s => s.RequestCancelPaidOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            var act = () => _sut.RequestCancelPaidOrder(Guid.NewGuid(), new CancelPaidOrderRequestDto());
            await act.Should().ThrowAsync<Exception>();
        }

        // ── bước 4: duyệt hủy + đơn thay thế + credit ────────────────────────

        [Fact]
        public async Task ApproveCancelAndCreateReplacement_Success_ReturnsOk()
        {
            _service.Setup(s => s.ApproveCancelAndCreateReplacementAsync(It.IsAny<Guid>(), _userId, It.IsAny<CreateReplacementOrderDto>()))
                .ReturnsAsync(new ReplacementOrderResponseDto());

            (await _sut.ApproveCancelAndCreateReplacement(Guid.NewGuid(), new CreateReplacementOrderDto()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ApproveCancelAndCreateReplacement_WhenOriginalOrderMissing_Returns404()
        {
            _service.Setup(s => s.ApproveCancelAndCreateReplacementAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateReplacementOrderDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ApproveCancelAndCreateReplacement(Guid.NewGuid(), new CreateReplacementOrderDto()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ApproveCancelAndCreateReplacement_WhenAlreadyApproved_Returns400()
        {
            _service.Setup(s => s.ApproveCancelAndCreateReplacementAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateReplacementOrderDto>()))
                .ThrowsAsync(new InvalidOperationException("Yeu cau da duoc duyet"));

            (await _sut.ApproveCancelAndCreateReplacement(Guid.NewGuid(), new CreateReplacementOrderDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ApproveCancelAndCreateReplacement_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ApproveCancelAndCreateReplacementAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateReplacementOrderDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ApproveCancelAndCreateReplacement(Guid.NewGuid(), new CreateReplacementOrderDto()))
                .StatusOf().Should().Be(400);
        }

        // ── đơn thay thế khi đổi hàng (WF-15) ────────────────────────────────

        [Fact]
        public async Task CreateExchangeReplacement_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreateExchangeReplacementOrderAsync(It.IsAny<Guid>(), _userId))
                .ReturnsAsync(new ReplacementOrderResponseDto());

            (await _sut.CreateExchangeReplacement(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateExchangeReplacement_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.CreateExchangeReplacementOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.CreateExchangeReplacement(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateExchangeReplacement_WhenRequestNotApproved_Returns400()
        {
            _service.Setup(s => s.CreateExchangeReplacementOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Yeu cau doi hang chua duoc duyet"));

            (await _sut.CreateExchangeReplacement(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateExchangeReplacement_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateExchangeReplacementOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CreateExchangeReplacement(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── bước 5: thu hồi hàng lỗi ─────────────────────────────────────────

        [Fact]
        public async Task GetPendingPickups_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetPendingPickupsAsync(_userId)).ReturnsAsync(new List<PendingPickupDto>());

            (await _sut.GetPendingPickups()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetPendingPickups_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetPendingPickupsAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetPendingPickups()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SchedulePickup_Success_ReturnsOk()
        {
            (await _sut.SchedulePickup(Guid.NewGuid(), new SchedulePickupRequestDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.SchedulePickupAsync(It.IsAny<Guid>(), _userId, It.IsAny<SchedulePickupRequestDto>()),
                Times.Once);
        }

        [Fact]
        public async Task SchedulePickup_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.SchedulePickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SchedulePickupRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.SchedulePickup(Guid.NewGuid(), new SchedulePickupRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task SchedulePickup_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.SchedulePickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SchedulePickupRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await _sut.SchedulePickup(Guid.NewGuid(), new SchedulePickupRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SchedulePickup_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.SchedulePickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SchedulePickupRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.SchedulePickup(Guid.NewGuid(), new SchedulePickupRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ConfirmPickup_Success_ReturnsOk()
        {
            (await _sut.ConfirmPickup(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.ConfirmPickupAsync(It.IsAny<Guid>(), _userId), Times.Once);
        }

        [Fact]
        public async Task ConfirmPickup_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.ConfirmPickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ConfirmPickup(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ConfirmPickup_WhenNotScheduledYet_Returns400()
        {
            _service.Setup(s => s.ConfirmPickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Chua len lich thu hoi"));

            (await _sut.ConfirmPickup(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ConfirmPickup_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ConfirmPickupAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ConfirmPickup(Guid.NewGuid())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ InventoryController (163 dòng, trước đó 16,4%).</summary>
    public class InventoryControllerTests
    {
        private readonly Mock<IInventoryService> _service = new();
        private readonly InventoryController _sut;
        private readonly Guid _staffId = Guid.NewGuid();

        public InventoryControllerTests()
            => _sut = new InventoryController(_service.Object).WithUser(_staffId, "WarehouseStaff");

        [Fact]
        public async Task GetInventoryReport_PassesAllFiltersThrough()
        {
            var warehouseId = Guid.NewGuid();
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 2, 1);
            _service.Setup(s => s.GetInventoryReportAsync(warehouseId, from, to)).ReturnsAsync(new InventoryReportDto());

            (await _sut.GetInventoryReport(warehouseId, from, to)).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetInventoryReportAsync(warehouseId, from, to), Times.Once);
        }

        [Fact]
        public async Task GetInventoryReport_WhenDateRangeInvalid_Returns400()
        {
            _service.Setup(s => s.GetInventoryReportAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
                .ThrowsAsync(new ArgumentException("toDate phai sau fromDate"));

            (await _sut.GetInventoryReport(null, null, null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetSlowMovingItems_UsesDefault14DaysWhenNotSupplied()
        {
            _service.Setup(s => s.GetSlowMovingItemsAsync(It.IsAny<Guid?>(), It.IsAny<int>()))
                .ReturnsAsync(new List<SlowMovingItemDto>());

            (await _sut.GetSlowMovingItems(null)).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSlowMovingItemsAsync(null, 14), Times.Once);
        }

        [Fact]
        public async Task GetSlowMovingItems_WhenDaysInvalid_Returns400()
        {
            _service.Setup(s => s.GetSlowMovingItemsAsync(It.IsAny<Guid?>(), It.IsAny<int>()))
                .ThrowsAsync(new ArgumentException("days phai > 0"));

            (await _sut.GetSlowMovingItems(null, days: -5)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitShiftCount_Success_ReturnsOkWithStaffIdFromToken()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), _staffId))
                .ReturnsAsync(new ShiftInventoryCountResultDto());

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SubmitShiftCount_WhenPayloadInvalid_Returns400()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new ArgumentException("danh sach dem rong"));

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitShiftCount_WhenShiftMissing_Returns404()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay ca"));

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task SubmitShiftCount_WhenStaffOfAnotherWarehouse_Returns403()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task SubmitShiftCount_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task SubmitShiftCount_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.SubmitShiftInventoryCountAsync(It.IsAny<ShiftInventoryCountRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitShiftCount_WithoutUserClaim_Returns403()
        {
            _sut.WithAnonymousUser();

            // Khác CartController/WarehouseController: action này CÓ catch UnauthorizedAccessException
            // riêng nên `GetUserId()` ném ra sẽ thành 403 chứ không rơi xuống catch(Exception) = 400.
            (await _sut.SubmitShiftCount(new ShiftInventoryCountRequestDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetWarehouseInventory_ClampsPagingToSafeRange()
        {
            _service.Setup(s => s.GetInventoryByWarehouseAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new PaginatedList<InventoryItemDto> { PageNumber = 1, PageSize = 10 });
            var warehouseId = Guid.NewGuid();

            (await _sut.GetWarehouseInventory(warehouseId, null, null, null, null, null, pageNumber: 0, pageSize: 5000))
                .StatusOf().Should().Be(200);

            _service.Verify(s => s.GetInventoryByWarehouseAsync(warehouseId, null, null, null, null, null, 1, 100),
                Times.Once, "pageNumber < 1 kẹp về 1, pageSize > 100 kẹp về 100");
        }

        [Fact]
        public async Task GetWarehouseInventory_WhenPageSizeNonPositive_FallsBackTo10()
        {
            _service.Setup(s => s.GetInventoryByWarehouseAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new PaginatedList<InventoryItemDto> { PageNumber = 2, PageSize = 10 });

            await _sut.GetWarehouseInventory(Guid.NewGuid(), "mang", 5, 50, null, null, pageNumber: 2, pageSize: 0);

            _service.Verify(s => s.GetInventoryByWarehouseAsync(It.IsAny<Guid>(), "mang", 5, 50, null, null, 2, 10),
                Times.Once);
        }

        [Fact]
        public async Task GetWarehouseInventory_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetInventoryByWarehouseAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int?>(),
                    It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.GetWarehouseInventory(Guid.NewGuid(), null, null, null, null, null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task AdjustInventory_Success_ReturnsOk()
        {
            (await _sut.AdjustInventory(Guid.NewGuid(), new AdjustInventoryRequest { NewQuantity = 50, Note = "kiem ke" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.AdjustInventoryAsync(It.IsAny<Guid>(), 50, "kiem ke", _staffId), Times.Once);
        }

        [Fact]
        public async Task AdjustInventory_WhenInventoryMissing_Returns404()
        {
            _service.Setup(s => s.AdjustInventoryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.AdjustInventory(Guid.NewGuid(), new AdjustInventoryRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task AdjustInventory_WhenStaffOfAnotherWarehouse_Returns403()
        {
            _service.Setup(s => s.AdjustInventoryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.AdjustInventory(Guid.NewGuid(), new AdjustInventoryRequest())).StatusOf().Should().Be(403,
                "thủ kho chỉ được chỉnh tồn kho mình phụ trách (CEO được miễn)");
        }

        [Fact]
        public async Task AdjustInventory_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.AdjustInventoryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.AdjustInventory(Guid.NewGuid(), new AdjustInventoryRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task AdjustInventory_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.AdjustInventoryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.AdjustInventory(Guid.NewGuid(), new AdjustInventoryRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task AddInventory_Success_ReturnsOk()
        {
            _service.Setup(s => s.AddProductToWarehouseAsync(It.IsAny<AddInventoryRequest>(), _staffId))
                .ReturnsAsync(new InventoryItemDto());

            (await _sut.AddInventory(new AddInventoryRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AddInventory_WhenProductMissing_Returns404()
        {
            _service.Setup(s => s.AddProductToWarehouseAsync(It.IsAny<AddInventoryRequest>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.AddInventory(new AddInventoryRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task AddInventory_WhenProductAlreadyInWarehouse_Returns409()
        {
            _service.Setup(s => s.AddProductToWarehouseAsync(It.IsAny<AddInventoryRequest>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("San pham da co trong kho nay"));

            (await _sut.AddInventory(new AddInventoryRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task AddInventory_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.AddProductToWarehouseAsync(It.IsAny<AddInventoryRequest>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.AddInventory(new AddInventoryRequest())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ AdminUsersController (117 dòng, trước đó 37,1%).</summary>
    public class AdminUsersControllerTests
    {
        private readonly Mock<IAdminUserService> _service = new();
        private readonly AdminUsersController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public AdminUsersControllerTests()
            => _sut = new AdminUsersController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task Search_Success_ReturnsOk()
        {
            _service.Setup(s => s.SearchAsync(It.IsAny<AdminUserQueryDto>()))
                .ReturnsAsync(new PagedResultDto<AdminUserDto>());

            (await _sut.Search(new AdminUserQueryDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Search_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.SearchAsync(It.IsAny<AdminUserQueryDto>())).ThrowsAsync(new Exception("loi"));

            (await _sut.Search(new AdminUserQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetById_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new AdminUserDto());

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetById_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetById_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_Success_PassesActorIdentityForAuditTrail()
        {
            _service.Setup(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new AdminUserDto());

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), _adminId,
                It.IsAny<string>(), It.IsAny<string?>()), Times.Once,
                "id người thao tác phải xuống service để ghi audit log");
        }

        [Fact]
        public async Task Create_WhenEmailDuplicated_Returns409()
        {
            _service.Setup(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Email da ton tai"));

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Create_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ChangeRole_Success_ReturnsOk()
        {
            _service.Setup(s => s.ChangeRoleAsync(It.IsAny<Guid>(), It.IsAny<ChangeUserRoleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new AdminUserDto());

            (await _sut.ChangeRole(Guid.NewGuid(), new ChangeUserRoleRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ChangeRole_WhenUserMissing_Returns404()
        {
            _service.Setup(s => s.ChangeRoleAsync(It.IsAny<Guid>(), It.IsAny<ChangeUserRoleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ChangeRole(Guid.NewGuid(), new ChangeUserRoleRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ChangeRole_WhenSelfDemotionBlocked_Returns409()
        {
            _service.Setup(s => s.ChangeRoleAsync(It.IsAny<Guid>(), It.IsAny<ChangeUserRoleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Khong the tu ha quyen chinh minh"));

            (await _sut.ChangeRole(Guid.NewGuid(), new ChangeUserRoleRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ChangeRole_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ChangeRoleAsync(It.IsAny<Guid>(), It.IsAny<ChangeUserRoleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ChangeRole(Guid.NewGuid(), new ChangeUserRoleRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SetStatus_Success_ReturnsOk()
        {
            _service.Setup(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new AdminUserDto());

            (await _sut.SetStatus(Guid.NewGuid(), new SetUserActiveStatusRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SetStatus_WhenUserMissing_Returns404()
        {
            _service.Setup(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.SetStatus(Guid.NewGuid(), new SetUserActiveStatusRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task SetStatus_WhenSelfDeactivationBlocked_Returns409()
        {
            _service.Setup(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Khong the tu khoa chinh minh"));

            (await _sut.SetStatus(Guid.NewGuid(), new SetUserActiveStatusRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task SetStatus_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.SetStatus(Guid.NewGuid(), new SetUserActiveStatusRequest())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>
    /// Case code-driven phủ MarketingPostController (187 dòng, trước đó 16,3%).
    /// Controller đọc `IConfiguration` để lấy secret callback nên test bơm cấu hình in-memory.
    /// </summary>
    public class MarketingPostControllerTests
    {
        private readonly Mock<IMarketingPostService> _service = new();
        private readonly Mock<IAiGeneratorService> _ai = new();
        private readonly Guid _userId = Guid.NewGuid();

        private MarketingPostController Build(string? callbackSecret = "secret-cua-make")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MakeCom:CallbackSecret"] = callbackSecret })
                .Build();
            return new MarketingPostController(_service.Object, _ai.Object, config).WithUser(_userId, "SalesStaff");
        }

        [Fact]
        public async Task GenerateOptions_Success_ReturnsOk()
        {
            _ai.Setup(s => s.GenerateMarketingOptionsAsync(It.IsAny<GenerateAiContentRequestDto>()))
                .ReturnsAsync(new GenerateAiContentResponseDto());

            (await Build().GenerateOptions(new GenerateAiContentRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GenerateOptions_WhenAiUnavailable_Returns400()
        {
            _ai.Setup(s => s.GenerateMarketingOptionsAsync(It.IsAny<GenerateAiContentRequestDto>()))
                .ThrowsAsync(new HttpRequestException("AI service unreachable"));

            (await Build().GenerateOptions(new GenerateAiContentRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetPosts_PassesRoleAndUserIdForScoping()
        {
            _service.Setup(s => s.GetPostsAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ReturnsAsync(new List<MarketingPostDto>());
            var productId = Guid.NewGuid();

            (await Build().GetPosts("Draft", productId)).StatusOf().Should().Be(200);

            _service.Verify(s => s.GetPostsAsync("Draft", productId, "SalesStaff", _userId), Times.Once,
                "service lọc bài theo vai trò nên role + userId phải xuống đúng");
        }

        [Fact]
        public async Task GetPosts_WithoutUserClaim_FallsBackToEmptyGuid()
        {
            _service.Setup(s => s.GetPostsAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ReturnsAsync(new List<MarketingPostDto>());
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.GetPosts(null, null)).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetPostsAsync(null, null, "", Guid.Empty), Times.Once);
        }

        [Fact]
        public async Task GetPosts_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetPostsAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetPosts(null, null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetPostById_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetPostByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new MarketingPostDto());

            (await Build().GetPostById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetPostById_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetPostByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().GetPostById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreatePost_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.CreatePost(new CreateMarketingPostDto())).StatusOf().Should().Be(401);
            _service.Verify(s => s.CreatePostAsync(It.IsAny<CreateMarketingPostDto>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task CreatePost_Success_ReturnsOk()
        {
            _service.Setup(s => s.CreatePostAsync(It.IsAny<CreateMarketingPostDto>(), _userId))
                .ReturnsAsync(new MarketingPostDto());

            (await Build().CreatePost(new CreateMarketingPostDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreatePost_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreatePostAsync(It.IsAny<CreateMarketingPostDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().CreatePost(new CreateMarketingPostDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdatePost_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.UpdatePost(Guid.NewGuid(), new UpdateMarketingPostDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task UpdatePost_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdatePostAsync(It.IsAny<Guid>(), It.IsAny<UpdateMarketingPostDto>(), _userId, "SalesStaff"))
                .ReturnsAsync(new MarketingPostDto());

            (await Build().UpdatePost(Guid.NewGuid(), new UpdateMarketingPostDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdatePost_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.UpdatePostAsync(It.IsAny<Guid>(), It.IsAny<UpdateMarketingPostDto>(),
                    It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().UpdatePost(Guid.NewGuid(), new UpdateMarketingPostDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task UpdatePost_WhenAlreadyPublished_Returns400()
        {
            _service.Setup(s => s.UpdatePostAsync(It.IsAny<Guid>(), It.IsAny<UpdateMarketingPostDto>(),
                    It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Bai da dang, khong sua duoc"));

            (await Build().UpdatePost(Guid.NewGuid(), new UpdateMarketingPostDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitPost_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.SubmitPost(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task SubmitPost_Success_ReturnsOk()
        {
            _service.Setup(s => s.SubmitPostAsync(It.IsAny<Guid>(), _userId, "SalesStaff"))
                .ReturnsAsync(new MarketingPostDto());

            (await Build().SubmitPost(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SubmitPost_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.SubmitPostAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().SubmitPost(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task SubmitPost_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.SubmitPostAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Bai khong o trang thai Nhap"));

            (await Build().SubmitPost(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task MakeDecision_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.MakeDecision(Guid.NewGuid(), new MarketingPostDecisionDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MakeDecision_Success_ReturnsOk()
        {
            _service.Setup(s => s.MakeDecisionAsync(It.IsAny<Guid>(), It.IsAny<MarketingPostDecisionDto>(), _userId))
                .ReturnsAsync(new MarketingPostDto());

            (await Build().MakeDecision(Guid.NewGuid(), new MarketingPostDecisionDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task MakeDecision_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.MakeDecisionAsync(It.IsAny<Guid>(), It.IsAny<MarketingPostDecisionDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Bai chua duoc gui duyet"));

            (await Build().MakeDecision(Guid.NewGuid(), new MarketingPostDecisionDto())).StatusOf().Should().Be(400);
        }

        // ── callback từ Make.com ─────────────────────────────────────────────
        // Secret trong cấu hình luôn khác rỗng ở các case dưới, nên nhánh kiểm tra
        // secret chạy tất định, KHÔNG phụ thuộc biến môi trường ASPNETCORE_ENVIRONMENT.

        [Fact]
        public async Task MakeWebhookCallback_WithoutSecretHeader_Returns401()
        {
            var result = await Build().MakeWebhookCallback(Guid.NewGuid(), new MakeWebhookCallbackDto());

            result.StatusOf().Should().Be(401,
                "không có secret thì bất kỳ user đăng nhập nào cũng giả mạo được kết quả đăng bài");
        }

        [Fact]
        public async Task MakeWebhookCallback_WithWrongSecret_Returns401()
        {
            var sut = Build().WithHeader("x-make-secret", "secret-sai");

            (await sut.MakeWebhookCallback(Guid.NewGuid(), new MakeWebhookCallbackDto())).StatusOf().Should().Be(401);
            _service.Verify(s => s.HandleMakeWebhookCallbackAsync(It.IsAny<Guid>(), It.IsAny<MakeWebhookCallbackDto>()),
                Times.Never);
        }

        [Fact]
        public async Task MakeWebhookCallback_WithCorrectSecret_ReturnsOk()
        {
            _service.Setup(s => s.HandleMakeWebhookCallbackAsync(It.IsAny<Guid>(), It.IsAny<MakeWebhookCallbackDto>()))
                .ReturnsAsync(new MarketingPostDto());
            var sut = Build().WithHeader("x-make-secret", "secret-cua-make");

            (await sut.MakeWebhookCallback(Guid.NewGuid(), new MakeWebhookCallbackDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task MakeWebhookCallback_WhenPostMissing_Returns400()
        {
            _service.Setup(s => s.HandleMakeWebhookCallbackAsync(It.IsAny<Guid>(), It.IsAny<MakeWebhookCallbackDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay bai"));
            var sut = Build().WithHeader("x-make-secret", "secret-cua-make");

            (await sut.MakeWebhookCallback(Guid.NewGuid(), new MakeWebhookCallbackDto())).StatusOf().Should().Be(400);
        }
    }
}
