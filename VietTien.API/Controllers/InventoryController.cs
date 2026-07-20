using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/inventory")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
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

        [HttpGet("{warehouseId}")]
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
