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
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
