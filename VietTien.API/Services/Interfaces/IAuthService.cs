using VietTien.API.DTOs.Auth;

namespace VietTien.API.Services.Interfaces
{
    public interface IAuthService
    {
        /// <summary>Đăng ký tài khoản mới, gửi OTP qua email</summary>
        Task<(bool Success, string Message)> RegisterAsync(RegisterDto dto);

        /// <summary>Xác minh OTP, kích hoạt tài khoản</summary>
        Task<(bool Success, string Message)> VerifyOtpAsync(VerifyOtpDto dto);

        /// <summary>Gửi lại mã OTP qua email</summary>
        Task<(bool Success, string Message)> ResendEmailOtpAsync(string email);

        /// <summary>Đăng nhập bằng email/mật khẩu</summary>
        Task<(bool Success, string Message, AuthResponseDto? Data)> LoginAsync(LoginDto dto);

        /// <summary>Đăng nhập bằng Google ID Token</summary>
        Task<(bool Success, string Message, AuthResponseDto? Data)> LoginWithGoogleAsync(GoogleLoginDto dto);

        /// <summary>Gửi link đặt lại mật khẩu qua email</summary>
        Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordDto dto);

        /// <summary>Đặt lại mật khẩu bằng token</summary>
        Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto);

        /// <summary>Làm mới Access Token bằng Refresh Token</summary>
        Task<(bool Success, string Message, AuthResponseDto? Data)> RefreshTokenAsync(RefreshTokenDto dto);

        /// <summary>Đăng xuất (thu hồi Refresh Token)</summary>
        Task<(bool Success, string Message)> LogoutAsync(Guid userId);

        /// <summary>Hoàn thiện hồ sơ sau khi đăng nhập bằng Google</summary>
        Task<(bool Success, string Message)> CompleteProfileAsync(Guid userId, CompleteProfileDto dto);

        /// <summary>Yêu cầu gửi mã OTP qua SMS</summary>
        Task<(bool Success, string Message)> RequestPhoneVerificationAsync(Guid userId, string phoneNumber);

        /// <summary>Xác thực mã OTP qua SMS</summary>
        Task<(bool Success, string Message)> VerifyPhoneOtpAsync(Guid userId, string otpCode, string phoneNumber);
    }
}
