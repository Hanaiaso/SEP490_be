using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/warehouse/orders")]
    [ApiController]
    [Authorize]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;
        private readonly ICloudinaryService _cloudinaryService;

        public WarehouseController(IWarehouseService warehouseService, ICloudinaryService cloudinaryService)
        {
            _warehouseService = warehouseService;
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

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff,SalesStaff,CEO,Admin")]
        public async Task<ActionResult<List<WarehouseOrderListDto>>> GetOrders([FromQuery] string tabType, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
                pageSize = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

                var result = await _warehouseService.GetOrdersForWarehouseAsync(tabType, pageNumber, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{orderId}/detail")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<ActionResult<WarehouseOrderDetailDto>> GetOrderDetail(Guid orderId)
        {
            try
            {
                var result = await _warehouseService.GetOrderDetailAsync(orderId);
                return Ok(result);
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

        [HttpPost("{orderId}/accept")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> AcceptOrder(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.AcceptOrderAsync(orderId, staffId);
                return Ok(new { message = "Nhận đơn hàng thành công, trạng thái đã chuyển sang Đang đóng gói." });
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

        [HttpPost("{orderId}/shortage-alert")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> ReportShortage(Guid orderId, [FromBody] ShortageAlertRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var staffId = GetUserId();
                await _warehouseService.ReportShortageAsync(orderId, staffId, request);
                return Ok(new { message = "Đã gửi cảnh báo thiếu hàng tới bộ phận Sales." });
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

        [HttpGet("pick-tasks")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> GetPickTasks([FromQuery] string tabType = "InProgress", [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
                pageSize = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

                var result = await _warehouseService.GetPickTasksAsync(tabType, pageNumber, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("pick-tasks/{pickTaskId}/detail")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> GetPickTaskDetail(Guid pickTaskId)
        {
            try
            {
                var result = await _warehouseService.GetPickTaskDetailAsync(pickTaskId);
                return Ok(result);
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

        [HttpPost("pick-tasks/{pickTaskId}/accept")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> AcceptPickTask(Guid pickTaskId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.AcceptPickTaskAsync(pickTaskId, staffId);
                return Ok(new { message = "Nhận lệnh xuất kho thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Lệnh xuất kho đã được nhân viên khác tiếp nhận. Vui lòng tải lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pick-tasks/{pickTaskId}/items/{productId}/pick-progress")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> UpdatePickTaskItemProgress(Guid pickTaskId, Guid productId, [FromForm] int packedQty, IFormFile? imageFile)
        {
            try
            {
                var staffId = GetUserId();
                string? imageUrl = null;

                if (imageFile != null && imageFile.Length > 0)
                {
                    imageUrl = await _cloudinaryService.UploadEvidenceAsync(imageFile, "viettien/warehouse-evidence");
                }

                await _warehouseService.UpdatePickTaskItemProgressAsync(pickTaskId, staffId, productId, packedQty, imageUrl);
                return Ok(new { message = "Cập nhật tiến độ thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Lệnh xuất kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pick-tasks/{pickTaskId}/complete")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> CompletePickTask(Guid pickTaskId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.CompletePickTaskAsync(pickTaskId, staffId);
                return Ok(new { message = "Hoàn tất lấy hàng (Picking) thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Lệnh xuất kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/consolidate")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> ConsolidateOrder(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.ConsolidateOrderAsync(orderId, staffId);
                return Ok(new { message = "Tập kết đơn hàng thành công." });
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

        [HttpPost("{orderId}/handover")]
        [Authorize(Roles = "WarehouseStaff,SalesStaff,CEO,Admin")]
        public async Task<IActionResult> HandoverOrder(Guid orderId, [FromBody] VietTien.API.DTOs.Warehouse.HandoverRequestDto request)
        {
            try
            {
                var staffId = GetUserId();

                if (!string.IsNullOrEmpty(request.WarehouseSignature) && request.WarehouseSignature.StartsWith("data:image"))
                {
                    var url = await _cloudinaryService.UploadBase64ImageAsync(request.WarehouseSignature, "viettien/handovers", $"warehouse_sig_{orderId}");
                    request.WarehouseSignature = url;
                }

                if (!string.IsNullOrEmpty(request.SalesSignature) && request.SalesSignature.StartsWith("data:image"))
                {
                    var url = await _cloudinaryService.UploadBase64ImageAsync(request.SalesSignature, "viettien/handovers", $"sales_sig_{orderId}");
                    request.SalesSignature = url;
                }

                await _warehouseService.HandoverOrderAsync(orderId, staffId, request);
                return Ok(new { message = "Bàn giao đơn hàng thành công." });
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

        [HttpPost("{orderId}/goods-issue")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> PostGoodsIssue(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.PostGoodsIssueAsync(orderId, staffId);
                return Ok(new { message = "Đã phát hành phiếu xuất kho và trừ tồn kho vật lý thành công." });
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

        // ─── FUL-08: GỘP PICK NHIỀU ĐƠN (multi-pick) — cần Sales Manager duyệt trước ───────────

        /// <summary>Nhân viên kho đề xuất gộp pick nhiều đơn cùng lúc, chờ Sales Manager duyệt.</summary>
        [HttpPost("multi-pick/request")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> RequestMultiPick([FromBody] MultiPickOrderIdsRequestDto dto)
        {
            try
            {
                var staffId = GetUserId();
                var result = await _warehouseService.RequestMultiPickAsync(staffId, dto.OrderIds);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Sales Manager duyệt/từ chối đề xuất gộp pick.</summary>
        [HttpPost("multi-pick/{id:guid}/decision")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> DecideMultiPick(Guid id, [FromBody] MultiPickDecisionRequestDto dto)
        {
            try
            {
                var managerId = GetUserId();
                var result = await _warehouseService.DecideMultiPickAsync(id, managerId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Thực thi gộp pick — chỉ khi đã có MultiPickApproval Approved khớp đúng danh sách đơn hàng.</summary>
        [HttpPost("multi-pick")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> ExecuteMultiPick([FromBody] MultiPickOrderIdsRequestDto dto)
        {
            var staffId = GetUserId();
            var result = await _warehouseService.ExecuteMultiPickAsync(staffId, dto.OrderIds);
            return Ok(result);
        }
    }
}
