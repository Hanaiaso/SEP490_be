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
        private readonly ILogger<AuthService> _logger;
        private readonly IGoogleTokenValidator _googleTokenValidator;
        private readonly ISystemConfigService _systemConfigService;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IEmailService emailService,
            ISmsService smsService,
            IOptions<JwtSettings> jwtSettings,
            IConfiguration configuration,
            ISalesAllocationService salesAllocationService,
            ILogger<AuthService> logger,
            IGoogleTokenValidator googleTokenValidator,
            ISystemConfigService systemConfigService)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _emailService = emailService;
            _smsService = smsService;
            _jwtSettings = jwtSettings.Value;
            _configuration = configuration;
            _googleClientId = configuration["GoogleSettings:ClientId"] ?? string.Empty;
            _salesAllocationService = salesAllocationService;
            _logger = logger;
            _googleTokenValidator = googleTokenValidator;
            _systemConfigService = systemConfigService;
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

            // User đã tạo và commit thành công ở trên -> lỗi gửi email OTP không được làm cả
            // request Register thất bại (client sẽ nhận "email đã được sử dụng" nếu thử lại,
            // trong khi chưa từng nhận OTP) -> chỉ log, vẫn báo thành công để khách bấm "gửi lại OTP".
            try
            {
                await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi email OTP đăng ký cho {Email}", user.Email);
            }

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

            if (!IsEmailOtpMatch(user.OtpCode, dto.OtpCode))
                return (false, "Mã OTP không chính xác.");

            if (user.OtpExpiry is null || user.OtpExpiry < DateTime.UtcNow)
                return (false, "Mã OTP đã hết hạn. Vui lòng đăng ký lại.");

            // Kích hoạt tài khoản
            user.IsEmailVerified = true;
            user.OtpCode = null;
            user.OtpExpiry = null;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            // OTP đã được xác minh và commit thành công ở trên (OtpCode đã null hoá) -> nếu gán Sale
            // round-robin lỗi, không được làm cả request VerifyOtp thất bại (client sẽ không thể verify
            // lại bằng OTP cũ, "kẹt" không có cách xử lý) -> chỉ log, tài khoản vẫn được kích hoạt.
            try
            {
                // WF-01: sau khi email verified, gán Sale phụ trách theo Round-robin
                await _salesAllocationService.AutoAssignCustomerAsync(user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tự động gán Sale phụ trách cho user {UserId}", user.Id);
            }

            return (true, "Xác minh email thành công. Bạn có thể đăng nhập ngay bây giờ.");
        }

        // ─── GỬI LẠI OTP ──────────────────────────────────────────────────────────

        private const int EmailOtpValidityMinutes = 5;
        private const int EmailOtpResendCooldownSeconds = 60;
        private const int EmailOtpMaxSendsPerWindow = 5;
        private static readonly TimeSpan EmailOtpSendWindow = TimeSpan.FromMinutes(30);
        private const int EmailOtpMaxSendsPerDay = 10;
        private static readonly TimeSpan EmailOtpDayWindow = TimeSpan.FromHours(24);
        private const string EmailOtpSentMessage = "Mã OTP mới đã được gửi. Vui lòng kiểm tra email.";

        public async Task<(bool Success, string Message)> ResendEmailOtpAsync(string email)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(email);

            // Không tiết lộ email có tồn tại trong hệ thống hay không (NFR-SEC03): nhánh email lạ
            // trả CÙNG thông điệp "đã gửi" như nhánh thành công thật, chỉ khác là không gửi mail.
            if (user is null)
                return (true, EmailOtpSentMessage);

            if (user.IsEmailVerified)
                return (false, "Tài khoản này đã được xác minh trước đó.");

            // Chặn gửi lại OTP trước 60 giây kể từ lần gửi trước (suy ra từ OtpExpiry - thời hạn hiệu lực).
            if (user.OtpExpiry.HasValue)
            {
                var lastSentAt = user.OtpExpiry.Value.AddMinutes(-EmailOtpValidityMinutes);
                var secondsSinceLastSend = (DateTime.UtcNow - lastSentAt).TotalSeconds;
                if (secondsSinceLastSend < EmailOtpResendCooldownSeconds)
                    return (false, "Vui lòng đợi ít nhất 60 giây trước khi yêu cầu gửi lại mã OTP.");
            }

            // Rate limit: tối đa 5 lần gửi trong 30 phút.
            if (user.EmailOtpWindowStart == null || DateTime.UtcNow - user.EmailOtpWindowStart.Value > EmailOtpSendWindow)
            {
                user.EmailOtpWindowStart = DateTime.UtcNow;
                user.EmailOtpSendCount = 0;
            }
            if (user.EmailOtpSendCount >= EmailOtpMaxSendsPerWindow)
                return (false, "Bạn đã vượt quá số lần gửi mã OTP cho phép. Vui lòng thử lại sau.");

            // Rate limit: tối đa 10 lần gửi trong 1 ngày.
            if (user.EmailOtpDayWindowStart == null || DateTime.UtcNow - user.EmailOtpDayWindowStart.Value > EmailOtpDayWindow)
            {
                user.EmailOtpDayWindowStart = DateTime.UtcNow;
                user.EmailOtpSendCountDaily = 0;
            }
            if (user.EmailOtpSendCountDaily >= EmailOtpMaxSendsPerDay)
                return (false, "Bạn đã vượt quá số lần gửi mã OTP cho phép trong ngày. Vui lòng thử lại vào ngày mai.");

            // Tạo OTP mới - lưu HASH, không lưu giá trị thô (NFR-SEC04).
            var otpCode = GenerateOtp();
            user.OtpCode = BCrypt.Net.BCrypt.HashPassword(otpCode);
            user.OtpExpiry = DateTime.UtcNow.AddMinutes(EmailOtpValidityMinutes);
            user.EmailOtpSendCount += 1;
            user.EmailOtpSendCountDaily += 1;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            try
            {
                await _emailService.SendOtpEmailAsync(user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi email OTP đăng ký lại cho {Email}", user.Email);
            }

            return (true, EmailOtpSentMessage);
        }

        /// <summary>
        /// So khớp OTP email với giá trị lưu trong DB. Hỗ trợ cả giá trị HASH (do ResendEmailOtpAsync
        /// cấp) lẫn giá trị THÔ cũ (do RegisterAsync cấp lúc đăng ký ban đầu) để không phá vỡ luồng
        /// verify hiện có.
        /// </summary>
        private static bool IsEmailOtpMatch(string? storedOtpCode, string otpCode)
        {
            if (string.IsNullOrEmpty(storedOtpCode)) return false;

            if (storedOtpCode.StartsWith("$2"))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(otpCode, storedOtpCode);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    return false;
                }
            }

            return storedOtpCode == otpCode;
        }

        // ─── ĐĂNG NHẬP ─────────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto? Data)> LoginAsync(LoginDto dto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(dto.Email);

            if (user is null)
                return (false, "Email hoặc mật khẩu không chính xác.", null);

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return (false, "Email hoặc mật khẩu không chính xác.", null);

            if (!user.IsActive)
                return (false, "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.", null);

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
                var googleClientId = await _systemConfigService.GetEffectiveValueAsync("GOOGLE_OAUTH_CLIENT_ID") ?? _googleClientId;
                payload = await _googleTokenValidator.ValidateAsync(dto.IdToken, googleClientId);
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

            if (!user.IsActive)
                return (false, "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.", null);

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

            // Tạo link reset (frontend URL từ cấu hình)
            var frontendUrl = _configuration["AppSettings:FrontendUrl"] ?? "https://viettien.store";
            var resetLink = $"{frontendUrl}/reset-password?token={resetToken}&email={Uri.EscapeDataString(user.Email)}";

            // Token đã tạo và commit thành công ở trên -> lỗi gửi email không được làm cả request
            // thất bại (message trả về vốn đã cố tình chung chung để tránh lộ user tồn tại), chỉ log.
            try
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, resetLink);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi email đặt lại mật khẩu cho {Email}", user.Email);
            }

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

        private const int PhoneOtpValidityMinutes = 5;
        private const int PhoneOtpResendCooldownSeconds = 60;
        private const int PhoneOtpMaxSendsPerWindow = 5;
        private static readonly TimeSpan PhoneOtpSendWindow = TimeSpan.FromMinutes(30);
        private const int PhoneOtpMaxFailedAttempts = 5;

        public async Task<(bool Success, string Message)> RequestPhoneVerificationAsync(Guid userId, string phoneNumber)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null) return (false, "Không tìm thấy người dùng.");

            if (user.IsPhoneVerified && user.PhoneNumber == phoneNumber)
                return (false, "Số điện thoại này đã được xác minh.");

            if (await _unitOfWork.Users.PhoneExistsAsync(phoneNumber) && user.PhoneNumber != phoneNumber)
                return (false, "Số điện thoại này đã được sử dụng bởi một tài khoản khác.");

            // Chặn gửi lại OTP trước 60 giây kể từ lần gửi trước (suy ra từ PhoneOtpExpiry - thời hạn hiệu lực).
            if (user.PhoneOtpExpiry.HasValue)
            {
                var lastSentAt = user.PhoneOtpExpiry.Value.AddMinutes(-PhoneOtpValidityMinutes);
                var secondsSinceLastSend = (DateTime.UtcNow - lastSentAt).TotalSeconds;
                if (secondsSinceLastSend < PhoneOtpResendCooldownSeconds)
                    return (false, "Vui lòng đợi ít nhất 60 giây trước khi yêu cầu gửi lại mã OTP.");
            }

            // Rate limit: tối đa 5 lần gửi trong 30 phút.
            if (user.PhoneOtpWindowStart == null || DateTime.UtcNow - user.PhoneOtpWindowStart.Value > PhoneOtpSendWindow)
            {
                user.PhoneOtpWindowStart = DateTime.UtcNow;
                user.PhoneOtpSendCount = 0;
            }
            if (user.PhoneOtpSendCount >= PhoneOtpMaxSendsPerWindow)
                return (false, "Bạn đã vượt quá số lần gửi mã OTP cho phép. Vui lòng thử lại sau.");

            var otpCode = new Random().Next(100000, 999999).ToString();
            user.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword(otpCode)}:{phoneNumber}";
            user.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(PhoneOtpValidityMinutes);
            user.PhoneOtpSendCount += 1;
            user.PhoneOtpFailedAttempts = 0;
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

            if (user.PhoneOtpFailedAttempts >= PhoneOtpMaxFailedAttempts)
                return (false, "Bạn đã nhập sai mã OTP quá số lần cho phép. Vui lòng yêu cầu gửi lại mã mới.");

            if (!IsPhoneOtpMatch(user.PhoneOtpCode, otpCode, phoneNumber))
            {
                user.PhoneOtpFailedAttempts += 1;
                _unitOfWork.Users.Update(user);
                await _unitOfWork.SaveChangesAsync();
                return (false, "Mã OTP không hợp lệ hoặc số điện thoại không khớp.");
            }

            if (user.PhoneOtpExpiry == null || user.PhoneOtpExpiry < DateTime.UtcNow)
                return (false, "Mã OTP đã hết hạn.");

            user.PhoneNumber = phoneNumber;
            user.IsPhoneVerified = true;
            user.PhoneOtpCode = null;
            user.PhoneOtpExpiry = null;
            user.PhoneOtpFailedAttempts = 0;

            _unitOfWork.Users.Update(user);
            await _unitOfWork.SaveChangesAsync();

            return (true, "Xác minh số điện thoại thành công.");
        }

        /// <summary>
        /// So khớp OTP đã băm lưu ở "hash:phoneNumber" với mã người dùng nhập.
        /// </summary>
        private static bool IsPhoneOtpMatch(string? storedOtpCode, string otpCode, string phoneNumber)
        {
            if (string.IsNullOrEmpty(storedOtpCode)) return false;

            var separatorIndex = storedOtpCode.LastIndexOf(':');
            if (separatorIndex < 0) return false;

            var storedHash = storedOtpCode[..separatorIndex];
            var storedPhone = storedOtpCode[(separatorIndex + 1)..];
            if (storedPhone != phoneNumber) return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(otpCode, storedHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
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

            // Chỉ kiểm tra profile completion cho Customer — các role khác không cần,
            // tránh 2 DB query thừa khi login cho SalesStaff, Admin, CEO...
            if (user.Role == SystemRole.Customer)
            {
                userInfo.IsProfileCompleted = await IsProfileCompletedAsync(user.Id);
            }
            else
            {
                userInfo.IsProfileCompleted = true;
            }

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
