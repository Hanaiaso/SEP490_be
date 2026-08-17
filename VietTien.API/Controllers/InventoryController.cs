using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Exceptions;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/inventory")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;
        private readonly IInventoryCountSessionService _inventoryCountSessionService;

        public InventoryController(IInventoryService inventoryService, IInventoryCountSessionService inventoryCountSessionService)
        {
            _inventoryService = inventoryService;
            _inventoryCountSessionService = inventoryCountSessionService;
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

        [HttpGet("report")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> GetInventoryReport(
            [FromQuery] Guid? warehouseId,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            try
            {
                var result = await _inventoryService.GetInventoryReportAsync(warehouseId, fromDate, toDate);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("slow-moving")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> GetSlowMovingItems([FromQuery] Guid? warehouseId, [FromQuery] int days = 14)
        {
            try
            {
                var result = await _inventoryService.GetSlowMovingItemsAsync(warehouseId, days);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("low-stock-alerts")]
        [Authorize(Roles = "WarehouseStaff,SalesManager,CEO,Admin")]
        public async Task<IActionResult> GetLowStockAlerts()
        {
            var result = await _inventoryService.GetLowStockAlertsAsync();
            return Ok(result);
        }

        [HttpGet("excess-stock-alerts")]
        [Authorize(Roles = "WarehouseStaff,SalesManager,CEO,Admin")]
        public async Task<IActionResult> GetExcessStockAlerts()
        {
            var result = await _inventoryService.GetExcessStockAlertsAsync();
            return Ok(result);
        }

        // ─── INV-01: alias tương thích ngược cho InventoryCountSessionService (DEF-L4-003) ─────
        // Đã hợp nhất về 1 hệ thống kiểm kê duy nhất (POST/GET /api/inventory-count-sessions...,
        // xem InventoryCountSessionController) — bỏ StockCountSession/StockCountLine riêng của
        // Nhóm C. 2 action dưới đây CHỈ giữ lại route/DTO cũ để không phá vỡ hợp đồng API đã publish,
        // logic thật ủy quyền hết cho IInventoryCountSessionService.

        [HttpPut("count-sessions/{id:guid}/theoretical")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> LockTheoretical(Guid id)
        {
            // Hệ thống hợp nhất chốt snapshot lý thuyết NGAY lúc mở phiên (OpenAsync) — không còn
            // bước "khóa" tách rời nữa, nên gọi lại action này trên 1 phiên đã tồn tại luôn có
            // nghĩa "sửa snapshot đã chốt" -> luôn 409 (đúng hành vi L3-INV-01 mong đợi).
            try
            {
                await _inventoryCountSessionService.GetByIdAsync(id, GetUserId());
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            throw new CountSnapshotStateInvalidException(
                "Phiên kiểm kê đã được khóa snapshot lý thuyết ngay khi mở, không thể khóa lại.");
        }

        [HttpPost("count-sessions/{id:guid}/lines")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> RecordCountLine(Guid id, [FromBody] RecordCountLineRequestDto dto)
        {
            if (dto.ActualQuantity < 0)
                return BadRequest(new { code = "COUNT_LINE_INVALID", message = "Số lượng đếm thực tế phải lớn hơn hoặc bằng 0." });

            try
            {
                var staffId = GetUserId();
                var session = await _inventoryCountSessionService.GetByIdAsync(id, staffId);
                var item = session.Items.FirstOrDefault(i => i.InventoryId == dto.InventoryId)
                    ?? throw new KeyNotFoundException("Dòng tồn kho này không nằm trong phiên kiểm kê.");

                var result = await _inventoryCountSessionService.RecordItemCountAsync(
                    id, item.Id, staffId, new RecordCountItemRequest { PhysicalQuantity = dto.ActualQuantity });
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
        }

        [HttpGet("{warehouseId:guid}")]
        public async Task<IActionResult> GetWarehouseInventory(
            Guid warehouseId, 
            [FromQuery] string? search,
            [FromQuery] int? minQty,
            [FromQuery] int? maxQty,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                pageNumber = pageNumber < 1 ? 1 : pageNumber;
                pageSize = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

                var result = await _inventoryService.GetInventoryByWarehouseAsync(warehouseId, search, minQty, maxQty, fromDate, toDate, pageNumber, pageSize);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{inventoryId}/adjust")]
        [Authorize(Roles = "WarehouseStaff,CEO")]
        public async Task<IActionResult> AdjustInventory(Guid inventoryId, [FromBody] AdjustInventoryRequest request)
        {
            try
            {
                var staffId = GetUserId();
                await _inventoryService.AdjustInventoryAsync(inventoryId, request.NewQuantity, request.Note, staffId);
                return Ok(new { message = "Cập nhật số lượng tồn kho thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Dữ liệu tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpPost("add")]
        [Authorize(Roles = "WarehouseStaff,CEO")]
        public async Task<IActionResult> AddInventory([FromBody] AddInventoryRequest request)
        {
            try
            {
                var staffId = GetUserId();
                var result = await _inventoryService.AddProductToWarehouseAsync(request, staffId);
                return Ok(new { message = "Thêm sản phẩm vào kho thành công.", data = result });
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
