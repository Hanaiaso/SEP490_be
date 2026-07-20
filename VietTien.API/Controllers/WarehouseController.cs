using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/warehouse/orders")]
    [ApiController]
    [Authorize] // Có thể thêm policy "WarehouseStaff" sau
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
        public async Task<ActionResult<List<WarehouseOrderListDto>>> GetOrders([FromQuery] string tabType, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var result = await _warehouseService.GetOrdersForWarehouseAsync(tabType, pageNumber, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{orderId}/detail")]
        public async Task<ActionResult<WarehouseOrderDetailDto>> GetOrderDetail(Guid orderId)
        {
            try
            {
                var result = await _warehouseService.GetOrderDetailAsync(orderId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/accept")]
        public async Task<IActionResult> AcceptOrder(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.AcceptOrderAsync(orderId, staffId);
                return Ok(new { message = "Nhận đơn hàng thành công, trạng thái đã chuyển sang Đang đóng gói." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/shortage-alert")]
        public async Task<IActionResult> ReportShortage(Guid orderId, [FromBody] ShortageAlertRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var staffId = GetUserId();
                await _warehouseService.ReportShortageAsync(orderId, staffId, request);
                return Ok(new { message = "Đã gửi cảnh báo thiếu hàng tới bộ phận Sales." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("pick-tasks")]
        public async Task<IActionResult> GetPickTasks([FromQuery] string tabType = "InProgress", [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                var result = await _warehouseService.GetPickTasksAsync(tabType, pageNumber, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("pick-tasks/{pickTaskId}/detail")]
        public async Task<IActionResult> GetPickTaskDetail(Guid pickTaskId)
        {
            try
            {
                var result = await _warehouseService.GetPickTaskDetailAsync(pickTaskId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pick-tasks/{pickTaskId}/accept")]
        public async Task<IActionResult> AcceptPickTask(Guid pickTaskId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.AcceptPickTaskAsync(pickTaskId, staffId);
                return Ok(new { message = "Nhận lệnh xuất kho thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pick-tasks/{pickTaskId}/items/{productId}/pick-progress")]
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("pick-tasks/{pickTaskId}/complete")]
        public async Task<IActionResult> CompletePickTask(Guid pickTaskId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.CompletePickTaskAsync(pickTaskId, staffId);
                return Ok(new { message = "Hoàn tất lấy hàng (Picking) thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/consolidate")]
        public async Task<IActionResult> ConsolidateOrder(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.ConsolidateOrderAsync(orderId, staffId);
                return Ok(new { message = "Tập kết đơn hàng thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/handover")]
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{orderId}/goods-issue")]
        public async Task<IActionResult> PostGoodsIssue(Guid orderId)
        {
            try
            {
                var staffId = GetUserId();
                await _warehouseService.PostGoodsIssueAsync(orderId, staffId);
                return Ok(new { message = "Đã phát hành phiếu xuất kho và trừ tồn kho vật lý thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
