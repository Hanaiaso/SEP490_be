using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // Audit log chỉ hỗ trợ tìm kiếm (GET) và export — không có action Update/Delete nào được expose.
    [Route("api/admin/audit-logs")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] AuditLogQueryDto query)
        {
            var result = await _auditLogService.SearchAsync(query);
            return Ok(result);
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] AuditLogQueryDto query)
        {
            var bytes = await _auditLogService.ExportCsvAsync(query);
            var fileName = $"audit-logs-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }
    }
}
