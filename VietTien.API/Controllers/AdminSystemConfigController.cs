using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/admin/system-configs")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminSystemConfigController : ControllerBase
    {
        private readonly ISystemConfigService _systemConfigService;

        public AdminSystemConfigController(ISystemConfigService systemConfigService)
        {
            _systemConfigService = systemConfigService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        private string GetUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _systemConfigService.GetAllWithEffectiveValuesAsync();
            return Ok(result);
        }

        [HttpGet("{key}/history")]
        public async Task<IActionResult> GetHistory(string key)
        {
            try
            {
                var result = await _systemConfigService.GetHistoryAsync(key);
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

        [HttpPut("{key}")]
        public async Task<IActionResult> Update(string key, [FromBody] UpdateSystemConfigRequest request)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _systemConfigService.SetValueAsync(key, request, GetUserId(), GetUserEmail(), ip);
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
    }
}
