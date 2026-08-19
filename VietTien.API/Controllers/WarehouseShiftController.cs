using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/warehouse-shifts")]
    [ApiController]
    [Authorize]
    public class WarehouseShiftController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public WarehouseShiftController(ApplicationDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
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
        public async Task<ActionResult<List<WarehouseShiftDto>>> GetShifts()
        {
            var shifts = await _context.WarehouseShifts.ToListAsync();
            return Ok(shifts.Select(ToDto).ToList());
        }

        [HttpPost]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<ActionResult<WarehouseShiftDto>> CreateShift([FromBody] CreateWarehouseShiftRequest request)
        {
            try
            {
                if (!TimeSpan.TryParseExact(request.StartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var start) ||
                    !TimeSpan.TryParseExact(request.EndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var end))
                {
                    return BadRequest(new { message = "Định dạng giờ không hợp lệ (HH:mm)." });
                }

                var nameExists = await _context.WarehouseShifts.AnyAsync(s => s.Name == request.Name.Trim());
                if (nameExists) throw new InvalidOperationException("Tên ca làm việc đã tồn tại.");

                var shift = new WarehouseShift
                {
                    Name = request.Name.Trim(),
                    StartTime = start,
                    EndTime = end,
                    Description = request.Description
                };
                _context.WarehouseShifts.Add(shift);
                await _context.SaveChangesAsync();

                var dto = ToDto(shift);
                await _auditLogService.LogAsync(
                    entityName: "WarehouseShift",
                    entityId: shift.Id.ToString(),
                    action: "CREATE",
                    actorUserId: GetUserId(),
                    actorEmail: GetUserEmail(),
                    actorRole: GetUserRole(),
                    before: null,
                    after: dto,
                    ipAddress: GetIp());

                return Ok(dto);
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

        [HttpPut("{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<ActionResult<WarehouseShiftDto>> UpdateShift(Guid id, [FromBody] UpdateWarehouseShiftRequest request)
        {
            try
            {
                var shift = await _context.WarehouseShifts.FindAsync(id);
                if (shift == null) return NotFound(new { message = "Không tìm thấy ca làm việc." });

                if (!TimeSpan.TryParseExact(request.StartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var start) ||
                    !TimeSpan.TryParseExact(request.EndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var end))
                {
                    return BadRequest(new { message = "Định dạng giờ không hợp lệ (HH:mm)." });
                }

                var nameExists = await _context.WarehouseShifts.AnyAsync(s => s.Id != id && s.Name == request.Name.Trim());
                if (nameExists) throw new InvalidOperationException("Tên ca làm việc đã tồn tại.");

                var before = ToDto(shift);
                shift.Name = request.Name.Trim();
                shift.StartTime = start;
                shift.EndTime = end;
                shift.Description = request.Description;
                await _context.SaveChangesAsync();

                var after = ToDto(shift);
                await _auditLogService.LogAsync(
                    entityName: "WarehouseShift",
                    entityId: shift.Id.ToString(),
                    action: "UPDATE",
                    actorUserId: GetUserId(),
                    actorEmail: GetUserEmail(),
                    actorRole: GetUserRole(),
                    before: before,
                    after: after,
                    ipAddress: GetIp());

                return Ok(after);
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

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<IActionResult> DeleteShift(Guid id)
        {
            var shift = await _context.WarehouseShifts.FindAsync(id);
            if (shift == null) return NotFound(new { message = "Không tìm thấy ca làm việc." });

            var before = ToDto(shift);
            _context.WarehouseShifts.Remove(shift);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                entityName: "WarehouseShift",
                entityId: id.ToString(),
                action: "DELETE",
                actorUserId: GetUserId(),
                actorEmail: GetUserEmail(),
                actorRole: GetUserRole(),
                before: before,
                after: null,
                ipAddress: GetIp());

            return NoContent();
        }

        private static WarehouseShiftDto ToDto(WarehouseShift s) => new WarehouseShiftDto
        {
            Id = s.Id,
            Name = s.Name,
            StartTime = s.StartTime.ToString(@"hh\:mm"),
            EndTime = s.EndTime.ToString(@"hh\:mm"),
            Description = s.Description
        };
    }
}
