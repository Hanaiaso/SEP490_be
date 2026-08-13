using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // DEF-L4-003: Phiên kiểm kê tồn kho theo Warehouse — chốt số lý thuyết khi mở phiên, đóng phiên
    // ("post") mới thực sự làm thay đổi tồn kho (trong ngưỡng) hoặc tạo đề xuất chờ CEO duyệt (vượt ngưỡng).
    [Route("api/inventory-count-sessions")]
    [ApiController]
    [Authorize]
    public class InventoryCountSessionController : ControllerBase
    {
        private readonly IInventoryCountSessionService _service;

        public InventoryCountSessionController(IInventoryCountSessionService service)
        {
            _service = service;
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

        private SystemRole GetUserRole()
        {
            var roleString = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(roleString) || !Enum.TryParse<SystemRole>(roleString, out var role))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return role;
        }

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> GetList([FromQuery] Guid? warehouseId, [FromQuery] string? status)
        {
            try
            {
                var result = await _service.GetListAsync(GetUserId(), GetUserRole(), warehouseId, status);
                return Ok(result);
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

        [HttpGet("{id:guid}")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _service.GetByIdAsync(id);
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

        [HttpPost]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> Open([FromBody] OpenInventoryCountSessionRequest request)
        {
            try
            {
                var result = await _service.OpenAsync(GetUserId(), request);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
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

        [HttpPut("{id:guid}/items/{itemId:guid}")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> RecordItemCount(Guid id, Guid itemId, [FromBody] RecordCountItemRequest request)
        {
            try
            {
                var result = await _service.RecordItemCountAsync(id, itemId, request);
                return Ok(result);
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

        [HttpPost("{id:guid}/close")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> Close(Guid id)
        {
            try
            {
                var result = await _service.CloseAsync(id, GetUserId());
                return Ok(result);
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
                return Conflict(new { message = "Dữ liệu tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
