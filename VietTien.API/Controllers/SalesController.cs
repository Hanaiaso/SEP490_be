using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Sales;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/sales")]
    [ApiController]
    [Authorize]
    public class SalesController : ControllerBase
    {
        private readonly ISalesAllocationService _allocationService;

        public SalesController(ISalesAllocationService allocationService)
        {
            _allocationService = allocationService;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ─── KHÁCH HÀNG CỦA TÔI (SAL-02 — Sales Staff) ────────────────────────────

        [HttpGet("my-customers")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> GetMyCustomers([FromQuery] MyCustomerQueryDto query)
        {
            var result = await _allocationService.GetMyCustomersAsync(GetUserId(), query);
            return Ok(result);
        }

        [HttpGet("my-customers/{id}")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> GetMyCustomerDetail(Guid id)
        {
            try
            {
                var result = await _allocationService.GetMyCustomerDetailAsync(GetUserId(), id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPut("my-customers/{id}/note")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> UpdateCustomerNote(Guid id, [FromBody] UpdateCustomerNoteRequest request)
        {
            try
            {
                await _allocationService.UpdateCustomerNoteAsync(GetUserId(), id, request.Note);
                return Ok(new { message = "Đã cập nhật ghi chú." });
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        // ─── QUẢN LÝ ROUND-ROBIN (MGR-02 — Sales Manager) ─────────────────────────

        [HttpGet("round-robin")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetRoundRobin()
        {
            var result = await _allocationService.GetRoundRobinStateAsync();
            return Ok(result);
        }

        [HttpPost("round-robin")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> AssignUnassignedCustomers()
        {
            try
            {
                var result = await _allocationService.AssignUnassignedCustomersAsync(GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("round-robin")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> UpdateRoundRobin([FromBody] UpdateRoundRobinRequest request)
        {
            try
            {
                var result = await _allocationService.UpdateRoundRobinAsync(GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
