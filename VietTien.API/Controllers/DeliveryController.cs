using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Exceptions;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/delivery")]
    [ApiController]
    [Authorize]
    public class DeliveryController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly IDeliveryTripService _deliveryTripService;

        public DeliveryController(IOrderService orderService, IDeliveryTripService deliveryTripService)
        {
            _orderService = orderService;
            _deliveryTripService = deliveryTripService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        // ─── BƯỚC 1: LẬP LỊCH XE ─────────────────────────────────────────────
        /// <summary>Sale lập lịch xe và gom đơn hàng vào ca giao</summary>
        [HttpPost("schedule")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> ScheduleDelivery([FromBody] ScheduleDeliveryRequestDto dto)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.ScheduleDeliveryAsync(userId, dto);
                return Ok(result);
            }
            catch (ScheduleConflictException ex)
            {
                // 409: FE dựa vào code này để báo khách đã gửi yêu cầu tới Sales Manager thay vì lỗi chung
                return Conflict(new { code = "SCHEDULE_CONFLICT", conflictId = ex.ConflictId, message = ex.Message });
            }
            catch (VehicleOverweightException ex)
            {
                // 409: FE dựa vào code này để hiện toast "Vượt tải trọng xe" riêng thay vì lỗi chung
                return Conflict(new { code = "VEHICLE_OVERWEIGHT", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── UC-34: SALES MANAGER XỬ LÝ XUNG ĐỘT LỊCH XE/CA ─────────────────
        [HttpGet("conflicts")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<ActionResult<List<DeliveryScheduleConflictDto>>> GetPendingConflicts()
        {
            try
            {
                var result = await _orderService.GetPendingDeliveryConflictsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("conflicts/{id:guid}/resolve")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> ResolveConflict(Guid id, [FromBody] ResolveDeliveryConflictRequestDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var managerId = GetUserId();
                var result = await _orderService.ResolveDeliveryConflictAsync(id, managerId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (VehicleOverweightException ex)
            {
                return Conflict(new { code = "VEHICLE_OVERWEIGHT", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── BƯỚC 1: LẤY DANH SÁCH ĐƠN GIAO HÀNG ────────────────────────────
        /// <summary>Lấy danh sách đơn hàng đang trong quá trình giao (của Sale đang đăng nhập)</summary>
        [HttpGet("orders")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<ActionResult<List<DeliveryOrderListDto>>> GetDeliveryOrders()
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetDeliveryOrdersAsync(userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Số lượng việc chờ xử lý cho từng mục con "Giao hàng" ở sidebar (badge) — cho Sale
        /// thấy trực quan chỗ nào có việc cần làm mà không phải mở từng trang.</summary>
        [HttpGet("sales-sidebar-counts")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<ActionResult<SalesDeliverySidebarCountsDto>> GetSalesSidebarCounts()
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetSalesDeliverySidebarCountsAsync(userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── BƯỚC 2: GHI NHẬN KẾT QUẢ GIAO HÀNG (POD + COD) ────────────────
        /// <summary>Sale ghi nhận kết quả giao hàng: chữ ký số, ảnh POD, số tiền thu</summary>
        [HttpPost("{orderId:guid}/complete")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> CompleteDelivery(Guid orderId, [FromBody] RecordDeliveryResultDto dto)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.RecordDeliveryResultAsync(orderId, userId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── BƯỚC 3: YÊU CẦU HỦY ĐƠN PAID (CR-06) ──────────────────────────
        /// <summary>Sale / SalesManager yêu cầu hủy đơn đã thanh toán</summary>
        [HttpPost("{orderId:guid}/request-cancel")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> RequestCancelPaidOrder(Guid orderId, [FromBody] CancelPaidOrderRequestDto dto)
        {
            try
            {
                var userId = GetUserId();
                await _orderService.RequestCancelPaidOrderAsync(orderId, userId, dto.Reason);
                return Ok(new { message = "Yêu cầu hủy đơn đã được gửi lên Sales Manager để phê duyệt." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            // GH-14: KHÔNG tự bắt InvalidOperationException ở đây — để ExceptionHandlingMiddleware map
            // đúng 409 Conflict cho lỗi sai trạng thái (trước đây bắt cục bộ trả nhầm 400).
        }

        // ─── BƯỚC 4: DUYỆT HỦY + TẠO ĐƠN THAY THẾ + CREDIT ─────────────────
        /// <summary>SalesManager phê duyệt hủy và tạo đơn thay thế. Phần chênh lệch chuyển vào ví Credit.</summary>
        [HttpPost("{originalOrderId:guid}/approve-cancel-replacement")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> ApproveCancelAndCreateReplacement(
            Guid originalOrderId,
            [FromBody] CreateReplacementOrderDto dto)
        {
            try
            {
                var managerId = GetUserId();
                var result = await _orderService.ApproveCancelAndCreateReplacementAsync(originalOrderId, managerId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // GH-15: 2 request duyệt huỷ song song — bên thua nhận 409, không tạo đơn thay thế thứ 2.
                return Conflict(new { message = "Đơn hàng đã được xử lý bởi một yêu cầu khác." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── ĐƠN THAY THẾ (ĐỔI HÀNG WF-15) ──────────────────────────────────────
        /// <summary>Sales Staff tạo đơn thay thế sau khi duyệt đổi hàng. Hệ thống tự động tính cấn trừ/Credit.</summary>
        [HttpPost("exchange/{requestId:guid}/replacement")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> CreateExchangeReplacement(Guid requestId)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.CreateExchangeReplacementOrderAsync(requestId, userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── BƯỚC 5: THU HỒI HÀNG LỖI TỪ KHÁCH HÀNG (PICKUP LOGISTICS) ──────────

        [HttpGet("pickups")]
        [Authorize(Roles = "SalesStaff,SalesManager,WarehouseStaff,Admin")]
        public async Task<ActionResult<List<PendingPickupDto>>> GetPendingPickups()
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetPendingPickupsAsync(userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pickups/{requestId:guid}/schedule")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> SchedulePickup(Guid requestId, [FromBody] SchedulePickupRequestDto dto)
        {
            try
            {
                var userId = GetUserId();
                await _orderService.SchedulePickupAsync(requestId, userId, dto);
                return Ok(new { message = "Đã lên lịch điều xe thu hồi thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (VehicleOverweightException ex)
            {
                return Conflict(new { code = "VEHICLE_OVERWEIGHT", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pickups/{requestId:guid}/confirm")]
        [Authorize(Roles = "SalesStaff,SalesManager,WarehouseStaff,Admin")]
        public async Task<IActionResult> ConfirmPickup(Guid requestId)
        {
            try
            {
                var userId = GetUserId();
                await _orderService.ConfirmPickupAsync(requestId, userId);
                return Ok(new { message = "Đã xác nhận lấy hàng thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── P2-6: SALES MANAGER XỬ LÝ ĐƠN BỊ KHÓA & CÔNG NỢ COD (UC-35) ──────

        /// <summary>Sales Manager xem danh sách đơn đang bị khóa do giao thất bại vượt ngưỡng</summary>
        [HttpGet("blocked-orders")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetBlockedOrders()
        {
            try
            {
                var result = await _orderService.GetBlockedOrdersAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Sales Manager mở khóa đơn để cho phép lên lịch giao lại</summary>
        [HttpPost("{orderId:guid}/unblock")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> UnblockOrder(Guid orderId, [FromBody] UnblockOrderRequest dto)
        {
            try
            {
                var managerId = GetUserId();
                await _orderService.UnblockOrderForRedeliveryAsync(orderId, managerId, dto.Reason);
                return Ok(new { message = "Đã mở khóa đơn hàng, có thể lên lịch giao lại." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Đơn hàng đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Sales Manager xem danh sách công nợ COD (mặc định: đang nợ + đã tất toán)</summary>
        [HttpGet("debts")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetDebts([FromQuery] string? status = null)
        {
            try
            {
                var result = await _orderService.GetDebtsAsync(status);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Sales Manager đánh dấu một khoản công nợ đã được tất toán</summary>
        [HttpPost("debts/{debtId:guid}/settle")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> SettleDebt(Guid debtId, [FromBody] SettleDebtRequest dto)
        {
            try
            {
                var managerId = GetUserId();
                await _orderService.SettleDebtAsync(debtId, managerId, dto.Note);
                return Ok(new { message = "Đã đánh dấu công nợ là đã tất toán." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ─── NHÓM C (DEL-01..07): DELIVERY TRIP (luồng chuyến giao, song song với luồng theo Order) ──

        /// <summary>Tạo chuyến giao hàng mới cho 1 xe/ca/ngày, gom các đơn hàng đã chọn vào chuyến.</summary>
        [HttpPost("trips")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> CreateTrip([FromBody] CreateDeliveryTripRequestDto dto)
        {
            var userId = GetUserId();
            var result = await _deliveryTripService.CreateTripAsync(userId, dto);
            return Ok(result);
        }

        /// <summary>Danh sách chuyến giao, lọc theo ngày/trạng thái (tuỳ chọn).</summary>
        [HttpGet("trips")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> GetTrips([FromQuery] DateTime? date, [FromQuery] string? status)
        {
            var result = await _deliveryTripService.GetTripsAsync(date, status);
            return Ok(result);
        }

        /// <summary>Bắt đầu bốc hàng lên xe — Scheduled → Loading. Có thể nhập kèm giờ xuất phát/đến dự kiến.</summary>
        [HttpPost("trips/{id:guid}/start-loading")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> StartLoading(Guid id, [FromBody] StartLoadingRequestDto dto)
        {
            var result = await _deliveryTripService.StartLoadingAsync(id, dto);
            return Ok(result);
        }

        /// <summary>Thêm đơn vào chuyến đang Bốc hàng (Loading) — chặn cứng nếu vượt tải trọng xe.</summary>
        [HttpPost("trips/{id:guid}/orders")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> AddOrdersToTrip(Guid id, [FromBody] AddOrdersToTripRequestDto dto)
        {
            var result = await _deliveryTripService.AddOrdersToTripAsync(id, dto);
            return Ok(result);
        }

        /// <summary>Rút 1 đơn khỏi chuyến đang Bốc hàng (Loading), để chuyển sang chuyến/xe khác.</summary>
        [HttpDelete("trips/{id:guid}/orders/{orderId:guid}")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> RemoveOrderFromTrip(Guid id, Guid orderId)
        {
            var result = await _deliveryTripService.RemoveOrderFromTripAsync(id, orderId);
            return Ok(result);
        }

        /// <summary>Hủy cả chuyến (chưa xuất phát) — nhả toàn bộ đơn về NotScheduled để xếp sang chuyến/xe khác.</summary>
        [HttpPost("trips/{id:guid}/cancel")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> CancelTrip(Guid id)
        {
            var result = await _deliveryTripService.CancelTripAsync(id);
            return Ok(result);
        }

        /// <summary>Xuất phát — yêu cầu chuyến đang Loading và mọi đơn trong chuyến đã có HandoverRecord Confirmed.</summary>
        [HttpPost("trips/{id:guid}/start")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> StartTrip(Guid id)
        {
            var result = await _deliveryTripService.StartTripAsync(id);
            return Ok(result);
        }

        /// <summary>Xem chi tiết 1 chuyến giao — chỉ người tạo chuyến hoặc SalesManager/Admin được xem.</summary>
        [HttpGet("trips/{id:guid}")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> GetTrip(Guid id)
        {
            var result = await _deliveryTripService.GetTripByIdAsync(id);

            var userId = GetUserId();
            if (result.CreatedByUserId != userId && !User.IsInRole("SalesManager") && !User.IsInRole("Admin"))
                return StatusCode(403, new { code = "DELIVERY_SCOPE_FORBIDDEN", message = "Bạn không có quyền xem chuyến giao này." });

            return Ok(result);
        }

        /// <summary>Ghi nhận kết quả 1 lần giao (POD nếu thành công, lý do nếu thất bại) cho 1 đơn trong chuyến.</summary>
        [HttpPost("attempts")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> RecordAttempt([FromBody] RecordDeliveryAttemptRequestDto dto)
        {
            try
            {
                var userId = GetUserId();
                var result = await _deliveryTripService.RecordAttemptAsync(userId, dto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                // POD (proof of delivery) thiếu/không hợp lệ -> 422, khác với 400 mặc định của middleware
                return UnprocessableEntity(new { code = "POD_INCOMPLETE_OR_INVALID", message = ex.Message });
            }
        }

        /// <summary>Ghi nhận thu tiền COD cho 1 đơn trong chuyến (có thể thu nhiều lần/1 phần).</summary>
        [HttpPost("collections")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> RecordCollection([FromBody] RecordCollectionRequestDto dto)
        {
            try
            {
                var result = await _deliveryTripService.RecordCollectionAsync(dto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { code = "COD_AMOUNT_INVALID", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "COD_NOT_ALLOWED_FOR_PAID_ORDER", message = ex.Message });
            }
        }
    }
}

