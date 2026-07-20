using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Auth;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }


        /// Đăng ký tài khoản mới. Sau khi đăng ký thành công, OTP sẽ được gửi qua email.

        [HttpPost("register")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message) = await _authService.RegisterAsync(dto);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }


        /// Xác minh OTP để kích hoạt tài khoản.

        [HttpPost("verify-otp")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message) = await _authService.VerifyOtpAsync(dto);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }


        /// Đăng nhập bằng email và mật khẩu.

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message, data) = await _authService.LoginAsync(dto);

            if (!success)
                return Unauthorized(new { message });

            return Ok(new { message, data });
        }


        /// Đăng nhập bằng Google (ID Token từ frontend).

        [HttpPost("google-login")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message, data) = await _authService.LoginWithGoogleAsync(dto);

            if (!success)
                return Unauthorized(new { message });

            return Ok(new { message, data });
        }


        /// Yêu cầu gửi email đặt lại mật khẩu.

        [HttpPost("forgot-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message) = await _authService.ForgotPasswordAsync(dto);

            // Luôn trả 200 để tránh lộ user tồn tại
            return Ok(new { message });
        }

        /// Đặt lại mật khẩu bằng token từ email.

        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message) = await _authService.ResetPasswordAsync(dto);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }


        /// Làm mới Access Token bằng Refresh Token.

        [HttpPost("refresh-token")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var (success, message, data) = await _authService.RefreshTokenAsync(dto);

            if (!success)
                return Unauthorized(new { message });

            return Ok(new { message, data });
        }


        /// Đăng xuất (thu hồi Refresh Token).

        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var (success, message) = await _authService.LogoutAsync(userId);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }


        /// Hoàn thiện hồ sơ sau khi đăng ký bằng Google (cập nhật số điện thoại).

        [HttpPut("complete-profile")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CompleteProfile([FromBody] CompleteProfileDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var (success, message) = await _authService.CompleteProfileAsync(userId, dto);

            if (!success)
                return BadRequest(new { message });

            return Ok(new { message });
        }

        /// <summary>Yêu cầu gửi mã OTP qua SMS</summary>
        [HttpPost("request-phone-otp")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RequestPhoneOtp([FromBody] RequestPhoneOtpDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var (success, message) = await _authService.RequestPhoneVerificationAsync(userId, dto.PhoneNumber);

            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        /// <summary>Xác thực mã OTP qua SMS</summary>
        [HttpPost("verify-phone-otp")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> VerifyPhoneOtp([FromBody] VerifyPhoneOtpDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Không xác định được người dùng." });

            var (success, message) = await _authService.VerifyPhoneOtpAsync(userId, dto.OtpCode, dto.PhoneNumber);

            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
