using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // Sales Manager kiểm soát mục tiêu doanh thu tháng của từng Sales Staff.
    [Route("api/sales-targets")]
    [ApiController]
    [Authorize(Roles = "SalesManager")]
    public class SalesTargetsController : ControllerBase
    {
        private readonly ISalesTargetService _salesTargetService;

        public SalesTargetsController(ISalesTargetService salesTargetService)
        {
            _salesTargetService = salesTargetService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        /// <summary>Danh sách toàn bộ Sales Staff kèm mục tiêu + doanh thu thực tế của 1 tháng. Mặc định tháng hiện tại.</summary>
        [HttpGet]
        public async Task<IActionResult> GetTeamTargets([FromQuery] int? year, [FromQuery] int? month)
        {
            var now = DateTime.UtcNow;
            var result = await _salesTargetService.GetTeamTargetsAsync(year ?? now.Year, month ?? now.Month);
            return Ok(result);
        }

        /// <summary>Đặt/cập nhật mục tiêu doanh thu tháng cho 1 Sales Staff.</summary>
        [HttpPost]
        public async Task<IActionResult> SetTarget([FromBody] SetSalesTargetRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _salesTargetService.SetTargetAsync(request, GetUserId());
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
        }
    }
}
