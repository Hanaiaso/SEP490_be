using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BCrypt.Net;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using VietTien.API.DTOs.Auth;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtService _jwtService;
        private readonly IEmailService _emailService;
        private readonly ISmsService _smsService;
        private readonly JwtSettings _jwtSettings;
        private readonly string _googleClientId;
        private readonly IConfiguration _configuration;
        private readonly ISalesAllocationService _salesAllocationService;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IEmailService emailService,
            ISmsService smsService,
            IOptions<JwtSettings> jwtSettings,
            IConfiguration configuration,
            ISalesAllocationService salesAllocationService)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _emailService = emailService;
            _smsService = smsService;
            _jwtSettings = jwtSettings.Value;
            _configuration = configuration;
            _googleClientId = configuration["GoogleSettings:ClientId"] ?? string.Empty;
            _salesAllocationService = salesAllocationService;
        }

        // ─── ĐĂNG KÝ ───────────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> RegisterAsync(RegisterDto dto)
        {
            // Kiểm tra email đã tồn tại
            if (await _unitOfWork.Users.EmailExistsAsync(dto.Email))
                return (false, "Email này đã được sử dụng.");

            // Kiểm tra số điện thoại đã tồn tại (nếu có nhập)
            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber) && await _unitOfWork.Users.PhoneExistsAsync(dto.PhoneNumber))
                return (false, "Số điện thoại này đã được sử dụng.");

            // WF-01: kiểm tra mã giới thiệu (nếu có) — không hợp lệ thì báo ngay để khách sửa hoặc bỏ trống (A2)
            Guid? referredByStaffId = null;
            if (!string.IsNullOrWhiteSpace(dto.ReferralCode))
            {
                var (staffId, error) = await _salesAllocationService.ResolveReferralStaffAsync(dto.ReferralCode);
                if (error != null)
                    return (false, error);
                referredByStaffId = staffId;
            }

            // Tạo OTP
            var otpCode = GenerateOtp();

            // Tạo user mới (chưa xác minh)
            var user = new User
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber ?? string.Empty,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = SystemRole.Customer,
                IsEmailVerified = false,
                OtpCode = otpCode,
                OtpExpiry = DateTime.UtcNow.AddMinutes(5),
                IsPhoneVerified = false,
                ReferredBySalesStaffId = referredByStaffId
            };

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            // Tạo sẵn CustomerProfile kèm MST (phục vụ nhận diện khách cũ khi xác minh)
            await _salesAllocationService.EnsureCustomerProfileAsync(user.Id, dto.TaxCode);

            // Gửi OTP qua email
            await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otpCode);

            return (true, "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã OTP xác minh tài khoản.");
        }

        // ─── XÁC MINH OTP ──────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> VerifyOtpAsync(VerifyOtpDto dto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(dto.Email);

            if (user is null)
                return (false, "Không tìm thấy tài khoản với email này.");

            if (user.IsEmailVerified)
                return (false, "Tài khoản này đã được xác minh trước đó.");

            if (user.OtpCode != dto.OtpCode)
                return (false, "Mã OTP không chính xác.");

            if (user.OtpExpiry is null || user.OtpExpiry < DateTime.UtcNow)
                return (false, "Mã OTP đã hết hạn. Vui lòng đăng ký lại.");

            // Kích hoạt tài khoản
            user.IsEmailVerified = true;
            user.OtpCode = null;
            user.OtpExpiry = null;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            // WF-01: sau khi email verified, gán Sale phụ trách theo Round-robin
            await _salesAllocationService.AutoAssignCustomerAsync(user.Id);

            return (true, "Xác minh email thành công. Bạn có thể đăng nhập ngay bây giờ.");
        }

        // ─── ĐĂNG NHẬP ─────────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto? Data)> LoginAsync(LoginDto dto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(dto.Email);

            if (user is null)
                return (false, "Email hoặc mật khẩu không chính xác.", null);

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return (false, "Email hoặc mật khẩu không chính xác.", null);

            if (!user.IsEmailVerified)
                return (false, "Tài khoản chưa được xác minh. Vui lòng kiểm tra email để nhập mã OTP.", null);

            var response = await IssueTokensAsync(user);
            return (true, "Đăng nhập thành công.", response);
        }

        // ─── ĐĂNG NHẬP GOOGLE ──────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto? Data)> LoginWithGoogleAsync(GoogleLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;

            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleClientId }
                };
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
            }
            catch (InvalidJwtException)
            {
                return (false, "Google ID Token không hợp lệ hoặc đã hết hạn.", null);
            }

            // Tìm user theo Google ID
            var user = await _unitOfWork.Users.GetByGoogleIdAsync(payload.Subject);

            if (user is null)
            {
                // Kiểm tra email đã tồn tại chưa (trường hợp liên kết tài khoản)
                user = await _unitOfWork.Users.GetByEmailAsync(payload.Email);

                if (user is not null)
                {
                    // Liên kết tài khoản cũ với Google
                    user.GoogleId = payload.Subject;
                    user.IsEmailVerified = true;
                    _unitOfWork.Users.Update(user);
                }
                else
                {
                    // Tạo tài khoản mới qua Google (tự động, không cần OTP)
                    user = new User
                    {
                        FullName = payload.Name ?? payload.Email,
                        Email = payload.Email.ToLower().Trim(), // Normalize để khớp với GetByEmailAsync
                        PhoneNumber = string.Empty,
                        PasswordHash = string.Empty,
                        GoogleId = payload.Subject.Trim(),
                        IsEmailVerified = true,
                        Role = SystemRole.Customer,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _unitOfWork.Users.AddAsync(user);
                }

                await _unitOfWork.SaveChangesAsync();

                // WF-01: Google OAuth được coi là email verified → gán Sale theo Round-robin
                await _salesAllocationService.AutoAssignCustomerAsync(user.Id);
            }

            var response = await IssueTokensAsync(user);
            return (true, "Đăng nhập bằng Google thành công.", response);
        }

        // ─── QUÊN MẬT KHẨU ─────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordDto dto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(dto.Email);

            // Luôn trả về thành công để tránh lộ thông tin user tồn tại
            if (user is null)
                return (true, "Nếu email tồn tại trong hệ thống, bạn sẽ nhận được hướng dẫn đặt lại mật khẩu.");

            // Tạo reset token an toàn
            var resetToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

            user.PasswordResetToken = resetToken;
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            // Tạo link reset (frontend URL)
            var frontendUrl = _configuration["AppSettings:FrontendUrl"] ?? "http://localhost:3000";
            var resetLink = $"{frontendUrl}/reset-password?token={resetToken}&email={Uri.EscapeDataString(user.Email)}";

            await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, resetLink);

            return (true, "Nếu email tồn tại trong hệ thống, bạn sẽ nhận được hướng dẫn đặt lại mật khẩu.");
        }

        // ─── ĐẶT LẠI MẬT KHẨU ─────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(dto.Email);

            if (user is null || user.PasswordResetToken != dto.Token)
                return (false, "Token đặt lại mật khẩu không hợp lệ.");

            if (user.PasswordResetTokenExpiry is null || user.PasswordResetTokenExpiry < DateTime.UtcNow)
                return (false, "Token đặt lại mật khẩu đã hết hạn. Vui lòng yêu cầu lại.");

            // Cập nhật mật khẩu
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            // Thu hồi tất cả refresh token hiện tại (bảo mật)
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return (true, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.");
        }

        // ─── REFRESH TOKEN ──────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto? Data)> RefreshTokenAsync(RefreshTokenDto dto)
        {
            var user = await _unitOfWork.Users.GetByRefreshTokenAsync(dto.RefreshToken);

            if (user is null)
                return (false, "Refresh token không hợp lệ.", null);

            if (user.RefreshTokenExpiryTime is null || user.RefreshTokenExpiryTime < DateTime.UtcNow)
                return (false, "Refresh token đã hết hạn. Vui lòng đăng nhập lại.", null);

            var response = await IssueTokensAsync(user);
            return (true, "Làm mới token thành công.", response);
        }

        // ─── ĐĂNG XUẤT ─────────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> LogoutAsync(Guid userId)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);

            if (user is null)
                return (false, "Không tìm thấy tài khoản.");

            // Thu hồi refresh token
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return (true, "Đăng xuất thành công.");
        }

        // ─── HOÀN THIỆN HỒ SƠ ─────────────────────────────────────────────────────

        public async Task<(bool Success, string Message)> CompleteProfileAsync(Guid userId, CompleteProfileDto dto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);

            if (user is null)
                return (false, "Không tìm thấy tài khoản.");

            // Kiểm tra số điện thoại đã tồn tại ở user khác chưa
            if (!string.IsNullOrEmpty(dto.PhoneNumber))
            {
                var phoneExists = await _unitOfWork.Users.PhoneExistsAsync(dto.PhoneNumber);
                if (phoneExists && user.PhoneNumber != dto.PhoneNumber)
                    return (false, "Số điện thoại này đã được sử dụng bởi tài khoản khác.");
            }

            // Xử lý mật khẩu (tuỳ chọn)
            if (!string.IsNullOrEmpty(dto.Password))
            {
                if (dto.Password != dto.ConfirmPassword)
                    return (false, "Mật khẩu xác nhận không khớp.");

                if (dto.Password.Length < 6)
                    return (false, "Mật khẩu phải có ít nhất 6 ký tự.");

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            }

            user.FullName = dto.FullName;
            user.PhoneNumber = dto.PhoneNumber;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return (true, "Hoàn thiện hồ sơ thành công.");
        }

        // ─── XÁC MINH SỐ ĐIỆN THOẠI QUA SMS ────────────────────────────────────────

        public async Task<(bool Success, string Message)> RequestPhoneVerificationAsync(Guid userId, string phoneNumber)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null) return (false, "Không tìm thấy người dùng.");

            if (user.IsPhoneVerified && user.PhoneNumber == phoneNumber)
                return (false, "Số điện thoại này đã được xác minh.");

            if (await _unitOfWork.Users.PhoneExistsAsync(phoneNumber) && user.PhoneNumber != phoneNumber)
                return (false, "Số điện thoại này đã được sử dụng bởi một tài khoản khác.");

            var otpCode = new Random().Next(100000, 999999).ToString();
            user.PhoneOtpCode = $"{otpCode}:{phoneNumber}";
            user.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5);
            // user.PhoneNumber is NOT updated here anymore
            user.IsPhoneVerified = false;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            // Sử dụng mẫu tin nhắn thử nghiệm bắt buộc của eSMS: "CODE la ma xac minh dang ky Baotrixemay cua ban"
            string message = $"{otpCode} la ma xac minh dang ky Baotrixemay cua ban";
            var result = await _smsService.SendSmsAsync(phoneNumber, message);

            if (!result.Success)
                return (false, result.ErrorMessage);

            return (true, "Đã gửi mã xác minh SMS. Vui lòng kiểm tra điện thoại.");
        }

        public async Task<(bool Success, string Message)> VerifyPhoneOtpAsync(Guid userId, string otpCode, string phoneNumber)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null) return (false, "Không tìm thấy người dùng.");

            if (user.PhoneOtpCode != $"{otpCode}:{phoneNumber}")
                return (false, "Mã OTP không hợp lệ hoặc số điện thoại không khớp.");

            if (user.PhoneOtpExpiry == null || user.PhoneOtpExpiry < DateTime.UtcNow)
                return (false, "Mã OTP đã hết hạn.");

            user.PhoneNumber = phoneNumber;
            user.IsPhoneVerified = true;
            user.PhoneOtpCode = null;
            user.PhoneOtpExpiry = null;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return (true, "Xác minh số điện thoại thành công.");
        }
        // ─── PRIVATE HELPERS ────────────────────────────────────────────────────────

        private async Task<AuthResponseDto> IssueTokensAsync(User user)
        {
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();
            var expiresAt = _jwtService.GetAccessTokenExpiry();

            // Lưu refresh token vào DB
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays);

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            var userInfo = UserInfoDto.FromUser(user);
            userInfo.IsProfileCompleted = await IsProfileCompletedAsync(user.Id);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                User = userInfo
            };
        }

        /// <summary>Hồ sơ đầy đủ = có CustomerProfile và có ít nhất 1 địa chỉ giao hàng.</summary>
        private async Task<bool> IsProfileCompletedAsync(Guid userId)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null) return false;
            return await _unitOfWork.Addresses.CountByCustomerProfileIdAsync(profile.Id) > 0;
        }

        private static string GenerateOtp()
        {
            // Dùng cryptographically secure random để tạo OTP 6 chữ số (000000–999999)
            var randomBytes = new byte[4];
            System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
            var randomNumber = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % 1_000_000;
            return randomNumber.ToString("D6"); // Luôn đủ 6 chữ số, kể cả số đầu là 0
        }
    }
}
