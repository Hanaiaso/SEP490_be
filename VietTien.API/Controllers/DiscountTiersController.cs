using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // Khung chiết khấu = cấu hình giá thuần túy -> Admin-only toàn bộ (không role nào khác cần đọc trực tiếp,
    // chiết khấu được áp dụng minh bạch ở server khi checkout qua OrderService.CalculateDiscountAsync).
    [Route("api/admin/discount-tiers")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class DiscountTiersController : ControllerBase
    {
        private readonly IDiscountTierService _discountTierService;

        public DiscountTiersController(IDiscountTierService discountTierService)
        {
            _discountTierService = discountTierService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        private string GetUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _discountTierService.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _discountTierService.GetByIdAsync(id);
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
        public async Task<IActionResult> Create([FromBody] CreateDiscountTierRequest request)
        {
            try
            {
                var result = await _discountTierService.CreateAsync(request, GetUserId(), GetUserEmail(), GetIp());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDiscountTierRequest request)
        {
            try
            {
                var result = await _discountTierService.UpdateAsync(id, request, GetUserId(), GetUserEmail(), GetIp());
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
