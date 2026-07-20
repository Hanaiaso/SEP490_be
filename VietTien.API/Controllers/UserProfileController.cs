using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.UserProfile;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/user")]
    [Authorize]
    [Produces("application/json")]
    public class UserProfileController : ControllerBase
    {
        private readonly IUserProfileService _userProfileService;
        private readonly IAddressService _addressService;

        public UserProfileController(IUserProfileService userProfileService, IAddressService addressService)
        {
            _userProfileService = userProfileService;
            _addressService = addressService;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private Guid GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (claim == null || !Guid.TryParse(claim.Value, out var userId))
                throw new UnauthorizedAccessException("Không xác định được người dùng.");
            return userId;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. THÔNG TIN CÁ NHÂN
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Lấy thông tin cá nhân</summary>
        [HttpGet("profile")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetUserId();
                var result = await _userProfileService.GetProfileAsync(userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { type = "NotFound", title = "Not Found", status = 404, detail = ex.Message });
            }
        }

        /// <summary>Cập nhật thông tin cá nhân</summary>
        [HttpPut("profile")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    type = "ValidationError",
                    title = "Validation Error",
                    status = 400,
                    detail = "Dữ liệu không hợp lệ",
                    errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                });
            }

            try
            {
                var userId = GetUserId();
                var result = await _userProfileService.UpdateProfileAsync(userId, dto);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Upload ảnh đại diện</summary>
        [HttpPost("avatar")]
        [ProducesResponseType(typeof(AvatarResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            try
            {
                var userId = GetUserId();
                var result = await _userProfileService.UploadAvatarAsync(userId, file);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { type = "BadRequest", title = "Bad Request", status = 400, detail = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Xóa ảnh đại diện</summary>
        [HttpDelete("avatar")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeleteAvatar()
        {
            try
            {
                var userId = GetUserId();
                await _userProfileService.DeleteAvatarAsync(userId);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Đổi mật khẩu</summary>
        [HttpPost("change-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    type = "ValidationError",
                    title = "Validation Error",
                    status = 400,
                    detail = "Mật khẩu mới không đáp ứng yêu cầu",
                    errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                });
            }

            try
            {
                var userId = GetUserId();
                await _userProfileService.ChangePasswordAsync(userId, dto);
                return Ok(new { message = "Đổi mật khẩu thành công" });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { type = "BadRequest", title = "Bad Request", status = 400, detail = ex.Message });
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. ĐỊA CHỈ GIAO HÀNG
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Lấy danh sách địa chỉ</summary>
        [HttpGet("addresses")]
        [ProducesResponseType(typeof(List<AddressDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAddresses()
        {
            try
            {
                var userId = GetUserId();
                var result = await _addressService.GetAddressesAsync(userId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Thêm địa chỉ mới</summary>
        [HttpPost("addresses")]
        [ProducesResponseType(typeof(AddressDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateAddress([FromBody] CreateAddressDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    type = "ValidationError",
                    title = "Validation Error",
                    status = 400,
                    detail = "Dữ liệu không hợp lệ",
                    errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                });
            }

            try
            {
                var userId = GetUserId();
                var result = await _addressService.CreateAddressAsync(userId, dto);
                return CreatedAtAction(nameof(GetAddresses), result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { type = "BadRequest", title = "Bad Request", status = 400, detail = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Cập nhật địa chỉ</summary>
        [HttpPut("addresses/{id}")]
        [ProducesResponseType(typeof(AddressDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] UpdateAddressDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    type = "ValidationError",
                    title = "Validation Error",
                    status = 400,
                    detail = "Dữ liệu không hợp lệ",
                    errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                });
            }

            try
            {
                var userId = GetUserId();
                var result = await _addressService.UpdateAddressAsync(userId, id, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { type = "NotFound", title = "Not Found", status = 404, detail = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Xóa địa chỉ</summary>
        [HttpDelete("addresses/{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteAddress(Guid id)
        {
            try
            {
                var userId = GetUserId();
                await _addressService.DeleteAddressAsync(userId, id);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { type = "BadRequest", title = "Bad Request", status = 400, detail = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { type = "NotFound", title = "Not Found", status = 404, detail = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }

        /// <summary>Đặt địa chỉ mặc định</summary>
        [HttpPatch("addresses/{id}/set-default")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetDefaultAddress(Guid id)
        {
            try
            {
                var userId = GetUserId();
                await _addressService.SetDefaultAddressAsync(userId, id);
                return Ok(new { message = "Đã đặt địa chỉ mặc định thành công" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { type = "NotFound", title = "Not Found", status = 404, detail = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { type = "Unauthorized", title = "Unauthorized", status = 401, detail = ex.Message });
            }
        }
    }
}
