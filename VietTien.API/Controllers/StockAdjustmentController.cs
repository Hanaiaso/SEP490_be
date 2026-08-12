using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // P0-1: Kiểm kê kho -> CEO duyệt điều chỉnh tồn kho (UC-47/UC-54)
    [Route("api/stock-adjustments")]
    [ApiController]
    [Authorize]
    public class StockAdjustmentController : ControllerBase
    {
        private readonly IStockAdjustmentService _stockAdjustmentService;

        public StockAdjustmentController(IStockAdjustmentService stockAdjustmentService)
        {
            _stockAdjustmentService = stockAdjustmentService;
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
        public async Task<IActionResult> GetList([FromQuery] string? status)
        {
            try
            {
                var result = await _stockAdjustmentService.GetListAsync(GetUserId(), GetUserRole(), status);
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
                var result = await _stockAdjustmentService.GetByIdAsync(id);
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
        public async Task<IActionResult> Create([FromBody] CreateStockAdjustmentRequest request)
        {
            try
            {
                var staffId = GetUserId();
                var result = await _stockAdjustmentService.CreateAsync(staffId, request);
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id:guid}/decision")]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<IActionResult> Decide(Guid id, [FromBody] StockAdjustmentDecisionRequest request)
        {
            try
            {
                var ceoId = GetUserId();
                var result = await _stockAdjustmentService.DecideAsync(id, ceoId, request);
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
