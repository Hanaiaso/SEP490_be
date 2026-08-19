using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // Cấp cho CEO xem/sửa đúng các tham số OwnerLevel="Admin/CEO" (vd ngưỡng báo giá, ngưỡng tồn
    // đọng theo thời gian) mà AdminSystemConfigController vốn đã đánh dấu là CEO cùng sở hữu nhưng
    // trước đây chưa từng có UI/API nào cho CEO chạm tới — CEO chỉ vào được Admin portal.
    // Không dùng chung route với Admin: GetForOwnerAsync/SetValueAsync(requiredOwnerToken) đã tự lọc
    // + chặn ở tầng service, nhưng tách controller riêng để không lỡ để lộ endpoint quản trị khác.
    [Route("api/ceo/system-configs")]
    [ApiController]
    [Authorize(Roles = "CEO,Admin")]
    public class CeoSystemConfigController : ControllerBase
    {
        private readonly ISystemConfigService _systemConfigService;

        public CeoSystemConfigController(ISystemConfigService systemConfigService)
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
            var result = await _systemConfigService.GetForOwnerAsync("CEO");
            return Ok(result);
        }

        [HttpGet("{key}/history")]
        public async Task<IActionResult> GetHistory(string key)
        {
            var owned = await _systemConfigService.GetForOwnerAsync("CEO");
            if (!owned.Any(c => c.Key == key))
                return Forbid();

            try
            {
                var result = await _systemConfigService.GetHistoryAsync(key);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPut("{key}")]
        public async Task<IActionResult> Update(string key, [FromBody] UpdateSystemConfigRequest request)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _systemConfigService.SetValueAsync(key, request, GetUserId(), GetUserEmail(), ip, requiredOwnerToken: "CEO");
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
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
