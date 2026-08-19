using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Supplier;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "CEO,Admin")]
    public class SuppliersController : ControllerBase
    {
        private readonly ISupplierService _supplierService;
        private readonly IAuditLogService _auditLogService;

        public SuppliersController(ISupplierService supplierService, IAuditLogService auditLogService)
        {
            _supplierService = supplierService;
            _auditLogService = auditLogService;
        }

        private Guid GetUserId()
        {
            var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(v) || !Guid.TryParse(v, out var id))
                throw new UnauthorizedAccessException("Invalid user token.");
            return id;
        }

        private string GetUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        private string? GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value;
        private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _supplierService.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _supplierService.GetByIdAsync(id);
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
        public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
        {
            try
            {
                var result = await _supplierService.CreateAsync(request);

                await _auditLogService.LogAsync(
                    entityName: "Supplier",
                    entityId: result.Id.ToString(),
                    action: "CREATE",
                    actorUserId: GetUserId(),
                    actorEmail: GetUserEmail(),
                    actorRole: GetUserRole(),
                    before: null,
                    after: result,
                    ipAddress: GetIp());

                return Ok(result);
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

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierRequest request)
        {
            try
            {
                var before = await _supplierService.GetByIdAsync(id);
                var result = await _supplierService.UpdateAsync(id, request);

                await _auditLogService.LogAsync(
                    entityName: "Supplier",
                    entityId: id.ToString(),
                    action: "UPDATE",
                    actorUserId: GetUserId(),
                    actorEmail: GetUserEmail(),
                    actorRole: GetUserRole(),
                    before: before,
                    after: result,
                    ipAddress: GetIp());

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
    }
}
