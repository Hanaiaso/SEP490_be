using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/delivery")]
    [ApiController]
    [Authorize]
    public class DeliveryController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public DeliveryController(IOrderService orderService)
        {
            _orderService = orderService;
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
    }
}

