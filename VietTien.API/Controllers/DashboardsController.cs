using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // business.md bước 6: mỗi role chỉ truy cập đúng phạm vi dữ liệu của mình -> mỗi action
    // có [Authorize(Roles=...)] riêng, KHÔNG dùng chung 1 role ở cấp controller.
    [Route("api/dashboards")]
    [ApiController]
    [Authorize]
    public class DashboardsController : ControllerBase
    {
        private readonly ISalesStaffDashboardService _salesStaffDashboardService;
        private readonly ISalesManagerDashboardService _salesManagerDashboardService;
        private readonly ICeoDashboardService _ceoDashboardService;
        private readonly IWarehouseDashboardService _warehouseDashboardService;

        public DashboardsController(
            ISalesStaffDashboardService salesStaffDashboardService,
            ISalesManagerDashboardService salesManagerDashboardService,
            ICeoDashboardService ceoDashboardService,
            IWarehouseDashboardService warehouseDashboardService)
        {
            _salesStaffDashboardService = salesStaffDashboardService;
            _salesManagerDashboardService = salesManagerDashboardService;
            _ceoDashboardService = ceoDashboardService;
            _warehouseDashboardService = warehouseDashboardService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        private static (DateTime From, DateTime To) ResolveRange(DateTime? from, DateTime? to)
        {
            var effectiveTo = to ?? DateTime.UtcNow;
            var effectiveFrom = from ?? effectiveTo.AddDays(-30);

            if (effectiveFrom > effectiveTo)
                throw new ArgumentException("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn hoặc bằng 'to'.");

            return (effectiveFrom, effectiveTo);
        }

        [HttpGet("sales-staff")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> GetSalesStaffDashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            try
            {
                var (f, t) = ResolveRange(from, to);
                var result = await _salesStaffDashboardService.GetDashboardAsync(GetUserId(), f, t);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("sales-manager")]
        [Authorize(Roles = "SalesManager")]
        public async Task<IActionResult> GetSalesManagerDashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            try
            {
                var (f, t) = ResolveRange(from, to);
                var result = await _salesManagerDashboardService.GetDashboardAsync(f, t);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("ceo")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> GetCeoDashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            try
            {
                var (f, t) = ResolveRange(from, to);
                var result = await _ceoDashboardService.GetDashboardAsync(f, t);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("warehouse-staff")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> GetWarehouseDashboard()
        {
            var result = await _warehouseDashboardService.GetDashboardAsync();
            return Ok(result);
        }
    }
}
