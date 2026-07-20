using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Payment;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    /// <summary>
    /// MGR-05 — Xử lý ngoại lệ thanh toán SePay
    /// Chỉ Sales Manager được phép gọi các endpoint này.
    /// </summary>
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly IManualPaymentService _manualPaymentService;

        public PaymentsController(IManualPaymentService manualPaymentService)
        {
            _manualPaymentService = manualPaymentService;
        }

        private Guid GetUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                throw new UnauthorizedAccessException("Token không hợp lệ.");
            return userId;
        }

        /// <summary>
        /// [MGR-05] Lấy danh sách ngoại lệ thanh toán SePay cần xử lý.
        /// Bao gồm: Pending quá 30 phút, PAID nhưng chưa phân bổ được tồn.
        /// </summary>
        [HttpGet("sepay-exceptions")]
        [Authorize(Roles = "SalesManager")]
        [ProducesResponseType(typeof(List<SePayExceptionItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetSePayExceptions(CancellationToken ct)
        {
            try
            {
                var result = await _manualPaymentService.GetExceptionsAsync(ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { code = "INTERNAL_ERROR", message = ex.Message });
            }
        }

        /// <summary>
        /// [MGR-05] Sales Manager xác nhận thanh toán SePay thủ công.
        /// POST /api/orders/{orderId}/manual-confirm
        /// </summary>
        [HttpPost("{orderId:guid}/manual-confirm")]
        [Authorize(Roles = "SalesManager")]
        [ProducesResponseType(typeof(ManualConfirmPaymentResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ManualConfirm(
            Guid orderId,
            [FromBody] ManualConfirmPaymentRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var salesManagerId = GetUserId();
                var result = await _manualPaymentService.ManualConfirmAsync(orderId, request, salesManagerId, ct);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                var code = ExtractCode(ex.Message, "PAYMENT_NOT_FOUND");
                return NotFound(new { code, message = ExtractMessage(ex.Message) });
            }
            catch (ArgumentException ex)
            {
                var code = ExtractCode(ex.Message, "VALIDATION_ERROR");
                return BadRequest(new { code, message = ExtractMessage(ex.Message) });
            }
            catch (InvalidOperationException ex)
            {
                var code = ExtractCode(ex.Message, "CONFLICT_ERROR");
                return Conflict(new { code, message = ExtractMessage(ex.Message) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = "INTERNAL_ERROR", message = ex.Message });
            }
        }

        /// <summary>
        /// [MGR-05] Sales Manager thử phân bổ lại tồn kho cho đơn PAID nhưng chưa allocation.
        /// POST /api/orders/{orderId}/retry-allocation
        /// Điều kiện: PaymentStatus = Paid, OrderStatus = PaidReviewRequired
        /// </summary>
        [HttpPost("{orderId:guid}/retry-allocation")]
        [Authorize(Roles = "SalesManager")]
        [ProducesResponseType(typeof(ManualConfirmPaymentResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RetryAllocation(
            Guid orderId,
            [FromBody] RetryAllocationRequest request,
            CancellationToken ct)
        {
            try
            {
                var salesManagerId = GetUserId();
                var result = await _manualPaymentService.RetryAllocationAsync(orderId, salesManagerId, request.Note, ct);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                var code = ExtractCode(ex.Message, "PAYMENT_NOT_FOUND");
                return NotFound(new { code, message = ExtractMessage(ex.Message) });
            }
            catch (InvalidOperationException ex)
            {
                var code = ExtractCode(ex.Message, "CONFLICT_ERROR");
                return Conflict(new { code, message = ExtractMessage(ex.Message) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { code = "UNAUTHORIZED", message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = "INTERNAL_ERROR", message = ex.Message });
            }
        }

        // ── Helper: parse "CODE: message" format từ exception ──
        private static string ExtractCode(string message, string fallback)
        {
            var idx = message.IndexOf(':');
            return idx > 0 ? message[..idx].Trim() : fallback;
        }

        private static string ExtractMessage(string message)
        {
            var idx = message.IndexOf(':');
            return idx > 0 ? message[(idx + 1)..].Trim() : message;
        }
    }
}
