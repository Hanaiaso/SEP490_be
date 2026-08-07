using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Order;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/orders")]
    [ApiController]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ICloudinaryService _cloudinaryService;

        public OrderController(IOrderService orderService, ICloudinaryService cloudinaryService)
        {
            _orderService = orderService;
            _cloudinaryService = cloudinaryService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return userId;
        }

        private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        [HttpGet("checkout-summary")]
        public async Task<ActionResult<OrderPreviewDto>> GetCheckoutSummary()
        {
            try
            {
                var userId = GetUserId();
                var preview = await _orderService.GetCheckoutSummaryAsync(userId);
                return Ok(preview);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("place-order")]
        public async Task<ActionResult<OrderResponseDto>> PlaceOrder([FromBody] PlaceOrderRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var userId = GetUserId();
                var response = await _orderService.PlaceOrderAsync(userId, request);
                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{orderId}/sepay-qr")]
        public async Task<ActionResult<SePayQrResponseDto>> GenerateSePayQr(Guid orderId)
        {
            try
            {
                var response = await _orderService.GenerateSePayQrAsync(orderId, GetUserId(), GetUserRole());
                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{orderId}/payment-status")]
        public async Task<ActionResult<PaymentStatusResponseDto>> GetPaymentStatus(Guid orderId)
        {
            try
            {
                var response = await _orderService.GetPaymentStatusAsync(orderId, GetUserId(), GetUserRole());
                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("place-direct-order")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<ActionResult<DirectOrderResponseDto>> PlaceDirectOrder([FromBody] PlaceDirectOrderRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var response = await _orderService.PlaceDirectOrderAsync(request);
                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("sales-dashboard")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<ActionResult<SalesDashboardStatsDto>> GetSalesDashboard()
        {
            try
            {
                // business.md bước 6: SalesStaff chỉ xem đúng phạm vi của mình; SalesManager/Admin giữ nguyên toàn hệ thống.
                Guid? scopedSalesStaffId = User.IsInRole("SalesStaff") ? GetUserId() : null;
                var stats = await _orderService.GetSalesDashboardStatsAsync(scopedSalesStaffId);
                return Ok(stats);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/confirm-payment")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> ConfirmPayment(Guid orderId)
        {
            try
            {
                await _orderService.ConfirmDirectOrderPaymentAsync(orderId);
                return Ok(new { message = "Thanh toán thành công." });
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

        [HttpPost("{orderId}/upload-pdf")]
        public async Task<IActionResult> UploadInvoicePdf(Guid orderId, [FromBody] UploadPdfRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                await _orderService.UploadInvoicePdfAsync(orderId, request.PdfBase64, GetUserId(), GetUserRole());
                return Ok(new { message = "Upload hóa đơn PDF thành công." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpGet("my-history")]
        public async Task<ActionResult<PagedResultDto<OrderHistoryItemDto>>> GetMyOrderHistory(
            [FromQuery] OrderHistoryQueryDto query)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetOrderHistoryAsync(userId, query);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpGet("my-history/{orderId:guid}")]
        public async Task<ActionResult<OrderHistoryDetailDto>> GetMyOrderDetail(Guid orderId)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetOrderDetailForCustomerAsync(userId, orderId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

 
        [AllowAnonymous]
        [HttpGet("track")]
        public async Task<ActionResult<OrderHistoryDetailDto>> TrackOrderPublic([FromQuery] string query)
        {
            try
            {
                var result = await _orderService.TrackOrderPublicAsync(query);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("my-stats")]
        public async Task<ActionResult<SpendingStatsDto>> GetMySpendingStats([FromQuery] SpendingStatsQueryDto query)
        {
            try
            {
                var userId = GetUserId();
                var result = await _orderService.GetSpendingStatsAsync(userId, query.Period);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpPost("{orderId:guid}/request-vat")]
        public async Task<IActionResult> RequestVatInvoice(Guid orderId)
        {
            try
            {
                var userId = GetUserId();
                await _orderService.RequestVatInvoiceAsync(userId, orderId);
                return Ok(new { message = "Yêu cầu hóa đơn VAT đã được ghi nhận. Đội kế toán sẽ xử lý trong thời gian sớm nhất." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("sales")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<ActionResult<PagedResultDto<SalesOrderListDto>>> GetSalesOrders([FromQuery] SalesOrderQueryDto query)
        {
            try
            {
                Guid? salesStaffId = null;
                if (User.IsInRole("SalesStaff"))
                {
                    salesStaffId = GetUserId();
                }

                var result = await _orderService.GetSalesOrdersAsync(query, salesStaffId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("sales/{id:guid}")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<ActionResult<SalesOrderDetailDto>> GetSalesOrderDetail(Guid id)
        {
            try
            {
                Guid? salesStaffId = null;
                if (User.IsInRole("SalesStaff"))
                {
                    salesStaffId = GetUserId();
                }

                var result = await _orderService.GetSalesOrderDetailAsync(id, salesStaffId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("sales/{id:guid}/confirm")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> ConfirmSalesOrder(Guid id)
        {
            try
            {
                var salesStaffId = GetUserId();
                await _orderService.ConfirmOrderAsync(id, salesStaffId);
                return Ok(new { message = "Xác nhận đơn hàng thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            // GH-09: KHÔNG tự bắt InvalidOperationException ở đây — để ExceptionHandlingMiddleware map
            // đúng 409 Conflict cho lỗi sai trạng thái (trước đây bắt cục bộ trả nhầm 400).
        }
        [HttpPost("sales/{id:guid}/reject")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> RejectSalesOrder(Guid id, [FromBody] RejectOrderRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var salesStaffId = GetUserId();
                await _orderService.RejectOrderAsync(id, salesStaffId, request.Reason);
                return Ok(new { message = "Đã từ chối đơn hàng thành công." });
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
        [HttpPost("{id:guid}/request-cancel")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> RequestCancelOrder(Guid id, [FromBody] RequestCancelOrderDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var customerId = GetUserId();
                await _orderService.RequestCancelOrderAsync(id, customerId, request);
                return Ok(new { message = "Yêu cầu hủy đơn hàng đã được gửi." });
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

        [HttpPost("sales/{id:guid}/process-cancel-request")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> ProcessCancelRequest(Guid id, [FromBody] ProcessCancelRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var salesStaffId = GetUserId();
                await _orderService.ProcessCancelRequestAsync(id, salesStaffId, request);
                return Ok(new { message = "Xử lý yêu cầu hủy đơn hàng thành công." });
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
        [HttpPost("{id:guid}/exchange-request")]
        [Authorize(Roles = "SalesManager,Admin,SalesStaff,Customer")]
        public async Task<IActionResult> CreateReturnExchangeRequest(Guid id, [FromBody] CreateReturnExchangeRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var userId = GetUserId();
                await _orderService.CreateReturnExchangeRequestAsync(id, userId, request);
                return Ok(new { message = "Đã gửi yêu cầu đổi/trả hàng thành công. Vui lòng chờ xử lý." });
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

        [HttpPost("exchange-request/{requestId:guid}/process")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> ProcessReturnExchangeRequest(Guid requestId, [FromBody] ProcessReturnExchangeRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var managerId = GetUserId();
                await _orderService.ProcessReturnExchangeRequestAsync(requestId, managerId, request);
                return Ok(new { message = "Đã xử lý yêu cầu đổi/trả hàng thành công." });
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

        [HttpPost("upload-evidence")]
        [Authorize(Roles = "SalesManager,Admin,SalesStaff,Customer")]
        public async Task<IActionResult> UploadEvidence(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Vui lòng chọn file." });

            try
            {
                var url = await _cloudinaryService.UploadEvidenceAsync(file, "viettien/return-exchange-evidence");
                return Ok(new { url });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
