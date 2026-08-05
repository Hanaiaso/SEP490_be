using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.DTOs.Auth;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: AuthService — L1-AUTH-01..16.
    /// L1-AUTH-10/11: LoginWithGoogleAsync giờ nhận IGoogleTokenValidator qua DI (bọc lệnh gọi tĩnh
    /// GoogleJsonWebSignature.ValidateAsync) nên mock được ở unit test — không còn BLOCKED.
    /// </summary>
    public class AuthServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IAddressRepository> _addressRepo = new();
        private readonly Mock<IJwtService> _jwt = new();
        private readonly Mock<IEmailService> _email = new();
        private readonly Mock<ISmsService> _sms = new();
        private readonly Mock<ISalesAllocationService> _salesAlloc = new();
        private readonly Mock<IGoogleTokenValidator> _googleValidator = new();
        private readonly AuthService _sut;

        public AuthServiceTests()
        {
            _uow.SetupGet(u => u.Users).Returns(_userRepo.Object);
            _uow.SetupGet(u => u.Addresses).Returns(_addressRepo.Object);
            _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

            _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<User>())).Returns("jwt");
            _jwt.Setup(j => j.GenerateRefreshToken()).Returns("rt");
            _jwt.Setup(j => j.GetAccessTokenExpiry()).Returns(DateTime.UtcNow.AddMinutes(15));

            _sut = new AuthService(
                _uow.Object, _jwt.Object, _email.Object, _sms.Object,
                TestConfig.JwtOptions(), TestConfig.Create(), _salesAlloc.Object,
                new Mock<ILogger<AuthService>>().Object, _googleValidator.Object);
        }

        /// <summary>DB chỉ lưu bản băm của refresh/reset token (khớp AuthService.HashToken).</summary>
        private static string Hash(string token)
            => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string ExtractTokenFromLink(string link)
        {
            const string marker = "token=";
            var start = link.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = link.IndexOf('&', start);
            var raw = end == -1 ? link[start..] : link[start..end];
            return Uri.UnescapeDataString(raw);
        }

        //  ▶ Block: RegisterAsync()

        // L1-AUTH-01 | EP-Invalid | Email đã tồn tại -> từ chối, không tạo user, không gửi OTP
        [Fact]
        public async Task L1_AUTH_01_Register_EmailExists_Rejected()
        {
            _userRepo.Setup(r => r.EmailExistsAsync("a@x.com")).ReturnsAsync(true);

            var (success, message) = await _sut.RegisterAsync(new RegisterDto { Email = "a@x.com", Password = "P@ss123" });

            success.Should().BeFalse();
            message.Should().Be("Email này đã được sử dụng.");
            _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
            _email.Verify(e => e.SendOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-02 | EP-Valid | Đăng ký hợp lệ -> user được tạo, password đã hash BCrypt, OTP email gửi 1 lần
        [Fact]
        public async Task L1_AUTH_02_Register_Valid_UserCreated_PasswordHashed_OtpSent()
        {
            _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            User? saved = null;
            _userRepo.Setup(r => r.AddAsync(It.IsAny<User>())).Callback<User>(u => saved = u).Returns(Task.CompletedTask);

            var (success, _) = await _sut.RegisterAsync(new RegisterDto
            {
                FullName = "Khách Test",
                Email = "new@x.com",
                PhoneNumber = "0912345678",
                Password = "P@ss123",
                ConfirmPassword = "P@ss123"
            });

            success.Should().BeTrue();
            _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
            saved.Should().NotBeNull();
            saved!.PasswordHash.Should().NotBe("P@ss123");
            BCrypt.Net.BCrypt.Verify("P@ss123", saved.PasswordHash).Should().BeTrue();
            _email.Verify(e => e.SendOtpEmailAsync("new@x.com", "Khách Test", It.IsAny<string>()), Times.Once);
        }

        // L1-AUTH-03 | EP-Invalid | Số điện thoại đã tồn tại -> từ chối, không tạo user
        [Fact]
        public async Task L1_AUTH_03_Register_PhoneExists_Rejected()
        {
            _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepo.Setup(r => r.PhoneExistsAsync("0912345678")).ReturnsAsync(true);

            var (success, message) = await _sut.RegisterAsync(new RegisterDto
            {
                Email = "new@x.com",
                PhoneNumber = "0912345678",
                Password = "P@ss123"
            });

            success.Should().BeFalse();
            message.Should().Be("Số điện thoại này đã được sử dụng.");
            _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
        }

        //  ▶ Block: VerifyOtpAsync()

        // L1-AUTH-04 | EP-Invalid | OTP sai -> từ chối, user chưa verified, không lưu thay đổi
        [Fact]
        public async Task L1_AUTH_04_VerifyOtp_WrongCode_Rejected()
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = false;
                u.OtpCode = "123456";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, message) = await _sut.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email, OtpCode = "999999" });

            success.Should().BeFalse();
            message.Should().Be("Mã OTP không chính xác.");
            user.IsEmailVerified.Should().BeFalse();
            _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
        }

        // L1-AUTH-05 | EP-Invalid | OTP hết hạn -> từ chối kèm thông báo hết hạn
        [Fact]
        public async Task L1_AUTH_05_VerifyOtp_Expired_Rejected()
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = false;
                u.OtpCode = "123456";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(-1); // đã quá hạn
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, message) = await _sut.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email, OtpCode = "123456" });

            success.Should().BeFalse();
            message.Should().Contain("hết hạn");
            user.IsEmailVerified.Should().BeFalse();
        }

        // L1-AUTH-06 | EP-Valid | OTP đúng trong hạn -> tài khoản verified, lưu DB, auto-assign Sale
        [Fact]
        public async Task L1_AUTH_06_VerifyOtp_Correct_AccountVerified()
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = false;
                u.OtpCode = "123456";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, _) = await _sut.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email, OtpCode = "123456" });

            success.Should().BeTrue();
            user.IsEmailVerified.Should().BeTrue();
            user.OtpCode.Should().BeNull();
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
            _salesAlloc.Verify(s => s.AutoAssignCustomerAsync(user.Id), Times.Once);
        }

        //  ▶ Block: LoginAsync()

        // L1-AUTH-07 | EP-Valid | Đăng nhập đúng -> phát access + refresh token, refresh token lưu trên user
        [Fact]
        public async Task L1_AUTH_07_Login_ValidCredentials_TokensIssued()
        {
            var user = TestData.User(); // password P@ss123, verified
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepo.Setup(r => r.GetCustomerProfileByUserIdAsync(user.Id)).ReturnsAsync((CustomerProfile?)null);

            var (success, _, data) = await _sut.LoginAsync(new LoginDto { Email = user.Email, Password = "P@ss123" });

            success.Should().BeTrue();
            data.Should().NotBeNull();
            data!.AccessToken.Should().Be("jwt");
            data.RefreshToken.Should().Be("rt"); // client vẫn nhận token gốc
            _jwt.Verify(j => j.GenerateAccessToken(user), Times.Once);
            user.RefreshToken.Should().Be(Hash("rt"), "DB chỉ được lưu bản băm của refresh token, không lưu token gốc");
        }

        // L1-AUTH-08 | EP-Invalid | Sai mật khẩu -> từ chối, không phát token, thông báo chung chung
        [Fact]
        public async Task L1_AUTH_08_Login_WrongPassword_RejectedWithGenericMessage()
        {
            var user = TestData.User();
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, message, data) = await _sut.LoginAsync(new LoginDto { Email = user.Email, Password = "WRONG" });

            success.Should().BeFalse();
            data.Should().BeNull();
            message.Should().Be("Email hoặc mật khẩu không chính xác."); // không tiết lộ field nào sai
            _jwt.Verify(j => j.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        }

        // L1-AUTH-09 | EP-Invalid | Email không tồn tại -> thông báo Y HỆT trường hợp sai mật khẩu
        [Fact]
        public async Task L1_AUTH_09_Login_UnknownEmail_SameGenericMessage()
        {
            _userRepo.Setup(r => r.GetByEmailAsync("ghost@x.com")).ReturnsAsync((User?)null);

            var (success, message, data) = await _sut.LoginAsync(new LoginDto { Email = "ghost@x.com", Password = "x" });

            success.Should().BeFalse();
            data.Should().BeNull();
            message.Should().Be("Email hoặc mật khẩu không chính xác."); // identical wording với L1-AUTH-08
            _jwt.Verify(j => j.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        }

        // L1-AUTH-10 | EP-Valid | Tài khoản Google mới -> tự tạo user + gán Sale round-robin.
        // ĐÃ SỬA: LoginWithGoogleAsync nay gọi qua IGoogleTokenValidator (mock được).
        [Fact]
        public async Task L1_AUTH_10_GoogleLogin_NewAccount_AutoCreated()
        {
            var payload = new GoogleJsonWebSignature.Payload { Subject = "google-sub-1", Email = "new@x.com", Name = "New User" };
            _googleValidator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(payload);
            _userRepo.Setup(r => r.GetByGoogleIdAsync("google-sub-1")).ReturnsAsync((User?)null);
            _userRepo.Setup(r => r.GetByEmailAsync("new@x.com")).ReturnsAsync((User?)null);

            var (success, _, data) = await _sut.LoginWithGoogleAsync(new GoogleLoginDto { IdToken = "fake-token" });

            success.Should().BeTrue();
            data.Should().NotBeNull();
            _userRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Email == "new@x.com" && u.GoogleId == "google-sub-1")), Times.Once);
            _salesAlloc.Verify(s => s.AutoAssignCustomerAsync(It.IsAny<Guid>()), Times.Once);
        }

        // L1-AUTH-11 | EP-Valid | Tài khoản Google đã tồn tại -> đăng nhập, KHÔNG tạo user trùng.
        [Fact]
        public async Task L1_AUTH_11_GoogleLogin_ExistingAccount_NoDuplicate()
        {
            var existing = TestData.User(u => { u.GoogleId = "google-sub-2"; u.IsActive = true; });
            var payload = new GoogleJsonWebSignature.Payload { Subject = "google-sub-2", Email = existing.Email, Name = existing.FullName };
            _googleValidator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(payload);
            _userRepo.Setup(r => r.GetByGoogleIdAsync("google-sub-2")).ReturnsAsync(existing);

            var (success, _, data) = await _sut.LoginWithGoogleAsync(new GoogleLoginDto { IdToken = "fake-token" });

            success.Should().BeTrue();
            data.Should().NotBeNull();
            _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
        }

        //  ▶ Block: RefreshTokenAsync()

        // L1-AUTH-12 | EP-Valid | Refresh token hợp lệ -> access token mới + refresh token XOAY (giá trị mới)
        [Fact]
        public async Task L1_AUTH_12_RefreshToken_Valid_NewTokenAndRotation()
        {
            var user = TestData.User(u =>
            {
                u.RefreshToken = Hash("rt-old");
                u.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(3);
            });
            _userRepo.Setup(r => r.GetByRefreshTokenAsync(Hash("rt-old"))).ReturnsAsync(user);
            _userRepo.Setup(r => r.GetCustomerProfileByUserIdAsync(user.Id)).ReturnsAsync((CustomerProfile?)null);
            _jwt.Setup(j => j.GenerateRefreshToken()).Returns("rt-new");

            var (success, _, data) = await _sut.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = "rt-old" });

            success.Should().BeTrue();
            data!.AccessToken.Should().Be("jwt");
            data.RefreshToken.Should().Be("rt-new"); // client nhận token gốc mới
            user.RefreshToken.Should().Be(Hash("rt-new"));
            user.RefreshToken.Should().NotBe(Hash("rt-old")); // rotation
        }

        // L1-AUTH-13 | EP-Invalid | Refresh token lạ/đã thu hồi -> từ chối, không phát token
        [Fact]
        public async Task L1_AUTH_13_RefreshToken_Unknown_Rejected()
        {
            _userRepo.Setup(r => r.GetByRefreshTokenAsync(Hash("rt-x"))).ReturnsAsync((User?)null);

            var (success, _, data) = await _sut.RefreshTokenAsync(new RefreshTokenDto { RefreshToken = "rt-x" });

            success.Should().BeFalse();
            data.Should().BeNull();
            _jwt.Verify(j => j.GenerateAccessToken(It.IsAny<User>()), Times.Never);
        }

        //  ▶ Block: ResetPasswordAsync() / LogoutAsync()

        // L1-AUTH-14 | EP-Invalid | Reset token sai -> mật khẩu không đổi, không lưu gì
        [Fact]
        public async Task L1_AUTH_14_ResetPassword_InvalidToken_NothingChanged()
        {
            var user = TestData.User(u => u.PasswordResetToken = Hash("tok-good"));
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
            var oldHash = user.PasswordHash;

            var (success, message) = await _sut.ResetPasswordAsync(new ResetPasswordDto
            {
                Email = user.Email,
                Token = "bad",
                NewPassword = "N3wPass!",
                ConfirmPassword = "N3wPass!"
            });

            success.Should().BeFalse();
            message.Should().Be("Token đặt lại mật khẩu không hợp lệ.");
            user.PasswordHash.Should().Be(oldHash);
            _uow.Verify(u => u.SaveChangesAsync(), Times.Never);
        }

        // L1-AUTH-15 | EP-Valid | Reset token đúng -> mật khẩu mới lưu dạng hash, token bị xóa (không dùng lại được)
        [Fact]
        public async Task L1_AUTH_15_ResetPassword_Valid_HashStoredAndTokenCleared()
        {
            var user = TestData.User(u =>
            {
                u.PasswordResetToken = Hash("tok");
                u.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, _) = await _sut.ResetPasswordAsync(new ResetPasswordDto
            {
                Email = user.Email,
                Token = "tok",
                NewPassword = "N3wPass!",
                ConfirmPassword = "N3wPass!"
            });

            success.Should().BeTrue();
            user.PasswordHash.Should().NotBe("N3wPass!");
            BCrypt.Net.BCrypt.Verify("N3wPass!", user.PasswordHash).Should().BeTrue();
            user.PasswordResetToken.Should().BeNull(); // token không thể tái sử dụng
            user.RefreshToken.Should().BeNull();       // thu hồi mọi phiên đăng nhập cũ
        }

        // L1-AUTH-16 | EP-Valid | Logout -> refresh token bị thu hồi
        [Fact]
        public async Task L1_AUTH_16_Logout_RefreshTokenRevoked()
        {
            var user = TestData.User(u => u.RefreshToken = "rt");
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            var (success, _) = await _sut.LogoutAsync(user.Id);

            success.Should().BeTrue();
            user.RefreshToken.Should().BeNull();
            user.RefreshTokenExpiryTime.Should().BeNull();
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        //  ▶ Block: ⊕ v2.3 — Gửi lại OTP qua email (ResendEmailOtpAsync)
        //  Feature thêm ở commit ec8f6cd, endpoint POST /auth/resend-otp.
        //  Trước đó FE phải hack bằng cách gọi lại /auth/register.

        // L1-AUTH-26 | EP-Invalid | Email không tồn tại -> KHÔNG gửi mail và trả thông điệp CHUNG,
        // không tiết lộ email có tồn tại trong hệ thống hay không.
        //
        // 🔴 SPEC GAP (doc v2.3): ResendEmailOtpAsync trả thẳng "Không tìm thấy tài khoản với email này."
        // -> kẻ tấn công dò được email nào đã đăng ký (user enumeration), vì thông điệp khác hẳn
        // trường hợp email tồn tại. Đối chiếu: ForgotPasswordAsync ở cùng service ĐÃ làm đúng —
        // luôn trả "Nếu email tồn tại trong hệ thống, bạn sẽ nhận được..." cho cả 2 nhánh.
        // Test ĐỎ cho tới khi ResendEmailOtp dùng cùng cách che thông tin đó.
        [Fact]
        public async Task L1_AUTH_26_ResendOtp_UnknownEmail_DoesNotLeakAccountExistence()
        {
            var existing = TestData.User(u => u.IsEmailVerified = false);
            _userRepo.Setup(r => r.GetByEmailAsync("ghost@x.com")).ReturnsAsync((User?)null);
            _userRepo.Setup(r => r.GetByEmailAsync(existing.Email)).ReturnsAsync(existing);

            var unknown = await _sut.ResendEmailOtpAsync("ghost@x.com");
            var known = await _sut.ResendEmailOtpAsync(existing.Email);

            // Không gửi mail cho email không tồn tại
            _email.Verify(e => e.SendOtpEmailAsync("ghost@x.com", It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // Thông điệp phải GIỐNG NHAU giữa 2 nhánh -> không suy ra được email nào có thật
            unknown.Message.Should().Be(known.Message,
                "thông điệp phải giống hệt nhau để chống dò email đã đăng ký (user enumeration)");
        }

        // L1-AUTH-27 | EP-Invalid | Tài khoản ĐÃ xác minh -> từ chối, không cấp OTP mới
        // (chặn lạm dụng endpoint công khai để spam mail tới người đã verify)
        [Fact]
        public async Task L1_AUTH_27_ResendOtp_AlreadyVerified_Rejected()
        {
            var user = TestData.User(u => { u.IsEmailVerified = true; u.OtpCode = null; });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, message) = await _sut.ResendEmailOtpAsync(user.Email);

            success.Should().BeFalse();
            message.Should().Contain("đã được xác minh");
            user.OtpCode.Should().BeNull("không được cấp OTP mới cho tài khoản đã verify");
            _email.Verify(e => e.SendOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-28 | EP-Valid | Hợp lệ -> sinh OTP MỚI, hạn 5 phút, gửi mail đúng 1 lần,
        // và giá trị lưu trong DB phải là HASH chứ không phải OTP thô.
        //
        // 🔴 SPEC GAP (doc v2.3): AuthService lưu OTP THÔ vào User.OtpCode — ai đọc được DB là dùng
        // được mã ngay. Cùng loại lỗ hổng với L1-AUTH-17 (OTP SMS).
        // Test ĐỎ cho tới khi băm giá trị OTP trước khi lưu.
        [Fact]
        public async Task L1_AUTH_28_ResendOtp_Valid_IssuesNewHashedOtpAndSendsMail()
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = false;
                u.OtpCode = "111111";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(-10); // OTP cũ đã hết hạn
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, _) = await _sut.ResendEmailOtpAsync(user.Email);

            success.Should().BeTrue();
            user.OtpCode.Should().NotBe("111111", "phải cấp mã MỚI, không dùng lại mã cũ");
            user.OtpExpiry.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);

            // Lấy mã THẬT đã gửi qua email rồi đối chiếu với giá trị lưu trong DB
            var sentOtp = _email.Invocations
                .Single(i => i.Method.Name == nameof(IEmailService.SendOtpEmailAsync))
                .Arguments[2] as string;
            sentOtp.Should().MatchRegex(@"^\d{6}$", "OTP gửi cho người dùng phải là 6 chữ số");

            user.OtpCode.Should().NotBe(sentOtp,
                "giá trị lưu trong DB phải là HASH của OTP, không phải mã thô (NFR-SEC04)");
        }

        // L1-AUTH-29 | BVA | Resend OTP email tại 59s / 60s / 61s so với lần gửi trước
        //
        // 🔴 SPEC GAP (doc v2.3): ResendEmailOtpAsync KHÔNG có bất kỳ cơ chế throttle nào —
        // bấm bao nhiêu lần cũng gửi. Cùng loại lỗ hổng với L1-AUTH-18 (OTP SMS).
        // Mốc thời gian suy ra từ OtpExpiry: OTP có hạn 5 phút, nên "gửi cách đây N giây"
        // tương ứng OtpExpiry = NOW + 5 phút − N giây.
        [Theory]
        [InlineData(59, false)] // chưa đủ 60s -> phải TỪ CHỐI
        [InlineData(60, true)]  // đúng 60s   -> cho gửi
        [InlineData(61, true)]  // quá 60s    -> cho gửi
        public async Task L1_AUTH_29_ResendOtp_ThrottledTo60Seconds(int secondsSinceLastSend, bool shouldSend)
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = false;
                u.OtpCode = "111111";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-secondsSinceLastSend);
            });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            var (success, _) = await _sut.ResendEmailOtpAsync(user.Email);

            success.Should().Be(shouldSend);
            _email.Verify(e => e.SendOtpEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>()),
                shouldSend ? Times.Once() : Times.Never());
        }

        // L1-AUTH-30 | BVA-Max+1 | Lần gửi thứ 6 trong 30 phút -> chặn theo rate limit
        //
        // 🔴 SPEC GAP (doc v2.3): không tồn tại bộ đếm số lần gửi OTP email.
        [Fact]
        public async Task L1_AUTH_30_ResendOtp_SixthSendWithin30Minutes_IsBlocked()
        {
            var user = TestData.User(u => u.IsEmailVerified = false);
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            // 5 lần đầu phải gửi được
            for (var i = 0; i < 5; i++)
            {
                user.OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90); // đủ giãn cách 60s
                var attempt = await _sut.ResendEmailOtpAsync(user.Email);
                attempt.Success.Should().BeTrue($"lần gửi thứ {i + 1} trong 30 phút vẫn hợp lệ");
            }
            _email.Invocations.Clear();

            // Lần thứ 6 phải bị chặn
            user.OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90);
            var sixth = await _sut.ResendEmailOtpAsync(user.Email);

            sixth.Success.Should().BeFalse("lần thứ 6 trong 30 phút phải bị chặn theo rate limit");
            _email.Verify(e => e.SendOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-31 | BVA-Max+1 | Lần gửi thứ 11 trong ngày -> chặn
        //
        // 🔴 SPEC GAP (doc v2.3): không có hạn mức theo ngày -> lạm dụng endpoint công khai để
        // spam mail (và tốn chi phí gửi) không giới hạn.
        [Fact]
        public async Task L1_AUTH_31_ResendOtp_EleventhSendInADay_IsBlocked()
        {
            var user = TestData.User(u => u.IsEmailVerified = false);
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            for (var i = 0; i < 10; i++)
            {
                user.OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90);
                await _sut.ResendEmailOtpAsync(user.Email);
            }
            _email.Invocations.Clear();

            user.OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90);
            var eleventh = await _sut.ResendEmailOtpAsync(user.Email);

            eleventh.Success.Should().BeFalse("lần thứ 11 trong ngày phải bị chặn");
            _email.Verify(e => e.SendOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        //  ▶ Block: ⊕ v2.3 — Quên mật khẩu (ForgotPasswordAsync)
        //  Doc v2.2 liệt kê method này ở header sheet nhưng KHÔNG có case nào — v2.3 đã bổ sung.

        // L1-AUTH-32 | EP-Invalid | Email không tồn tại -> phản hồi GIỐNG HỆT case email tồn tại,
        // không gửi mail, không tạo reset token (chống user enumeration).
        [Fact]
        public async Task L1_AUTH_32_ForgotPassword_UnknownEmail_DoesNotLeakAccountExistence()
        {
            var existing = TestData.User();
            _userRepo.Setup(r => r.GetByEmailAsync("ghost@x.com")).ReturnsAsync((User?)null);
            _userRepo.Setup(r => r.GetByEmailAsync(existing.Email)).ReturnsAsync(existing);

            var unknown = await _sut.ForgotPasswordAsync(new ForgotPasswordDto { Email = "ghost@x.com" });
            var known = await _sut.ForgotPasswordAsync(new ForgotPasswordDto { Email = existing.Email });

            unknown.Success.Should().Be(known.Success);
            unknown.Message.Should().Be(known.Message,
                "phản hồi phải giống hệt nhau để không suy ra được email nào đã đăng ký");

            _email.Verify(e => e.SendPasswordResetEmailAsync("ghost@x.com", It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-33 | EP-Valid | Email hợp lệ -> sinh reset token khó đoán, gửi đúng 1 mail,
        // link chứa token; token có hạn.
        [Fact]
        public async Task L1_AUTH_33_ForgotPassword_Valid_IssuesResetTokenAndSendsOneMail()
        {
            var user = TestData.User(u => { u.PasswordResetToken = null; u.PasswordResetTokenExpiry = null; });
            _userRepo.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

            string? capturedLink = null;
            _email.Setup(e => e.SendPasswordResetEmailAsync(user.Email, user.FullName, It.IsAny<string>()))
                .Callback<string, string, string>((_, _, link) => capturedLink = link)
                .Returns(Task.CompletedTask);

            var (success, _) = await _sut.ForgotPasswordAsync(new ForgotPasswordDto { Email = user.Email });

            success.Should().BeTrue();
            user.PasswordResetToken.Should().NotBeNullOrWhiteSpace();
            user.PasswordResetTokenExpiry.Should().NotBeNull().And.BeAfter(DateTime.UtcNow);

            capturedLink.Should().NotBeNullOrWhiteSpace("link đặt lại mật khẩu phải được gửi");
            var rawToken = ExtractTokenFromLink(capturedLink!);
            rawToken.Length.Should().BeGreaterThan(16, "token phải đủ dài để không đoán được");
            rawToken.Should().NotBe(user.PasswordResetToken, "DB không được lưu token gốc, chỉ lưu bản băm");
            Hash(rawToken).Should().Be(user.PasswordResetToken, "bản băm của token trong link phải khớp giá trị lưu DB");

            _email.Verify(e => e.SendPasswordResetEmailAsync(user.Email, user.FullName, It.IsAny<string>()),
                Times.Once, "phải gửi đúng 1 mail chứa link đặt lại mật khẩu");
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        //  ▶ Block: ⊕ v2.1 — Xác thực SĐT qua SMS
        //  RequestPhoneVerificationAsync / VerifyPhoneOtpAsync / CompleteProfileAsync
        //
        //  ⚠ Signature thật KHÔNG có tham số `purpose` như doc mô tả:
        //     RequestPhoneVerificationAsync(Guid userId, string phoneNumber)
        //     VerifyPhoneOtpAsync(Guid userId, string otpCode, string phoneNumber)

        /// <summary>User đã verify email, đăng ký sẵn với _userRepo và đặt sẵn OTP nếu cần.</summary>
        private User SeedUserForPhoneFlow(Action<User>? mutate = null)
        {
            var user = TestData.User(u =>
            {
                u.IsEmailVerified = true;
                u.IsPhoneVerified = false;
                mutate?.Invoke(u);
            });
            _userRepo.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            return user;
        }

        // L1-AUTH-17 | EP-Valid | Gửi OTP xác thực SĐT -> gọi SMS đúng 1 lần và LƯU DẠNG HASH
        // 🔴 SPEC GAP v2.2: AuthService lưu OTP THÔ vào User.PhoneOtpCode dưới dạng "123456:0912345678".
        // NFR-SEC04 yêu cầu lưu hash. Test ĐỎ cho tới khi băm giá trị OTP trước khi lưu.
        [Fact]
        public async Task L1_AUTH_17_RequestPhoneVerification_StoresHashAndSendsSmsOnce()
        {
            var user = SeedUserForPhoneFlow();
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sms.Setup(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((true, string.Empty));

            var (success, _) = await _sut.RequestPhoneVerificationAsync(user.Id, "0912345678");

            success.Should().BeTrue();
            _sms.Verify(s => s.SendSmsAsync("0912345678", It.IsAny<string>()), Times.Once);

            // Trích 6 chữ số OTP từ nội dung SMS đã gửi rồi đối chiếu với giá trị lưu trong DB.
            var sentMessage = _sms.Invocations.Single().Arguments[1] as string;
            var otpInSms = System.Text.RegularExpressions.Regex.Match(sentMessage!, @"\d{6}").Value;
            user.PhoneOtpCode.Should().NotContain(otpInSms, "OTP phải được lưu dạng HASH, không phải giá trị thô");
        }

        // L1-AUTH-18 | BVA-Min-1 | Gửi lại OTP trước 60 giây -> từ chối, KHÔNG gọi SMS
        // 🔴 SPEC GAP v2.2: RequestPhoneVerificationAsync không hề có cơ chế chặn resend theo thời gian.
        // FT-01 BV-02 yêu cầu resend >= 60s. Test ĐỎ cho tới khi bổ sung throttle.
        [Fact]
        public async Task L1_AUTH_18_ResendBefore60Seconds_IsRejected()
        {
            // Lần gửi trước cách đây 59 giây => OTP còn hạn 5 phút, tức PhoneOtpExpiry = now + 4'01"
            var user = SeedUserForPhoneFlow(u =>
            {
                u.PhoneOtpCode = "111111:0912345678";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-59);
            });
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sms.Setup(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((true, string.Empty));

            var (success, _) = await _sut.RequestPhoneVerificationAsync(user.Id, "0912345678");

            success.Should().BeFalse("chưa đủ 60 giây kể từ lần gửi trước");
            _sms.Verify(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-19 | BVA-Min | Gửi lại OTP sau đúng 60 giây -> cho phép, SMS được gọi 1 lần
        [Fact]
        public async Task L1_AUTH_19_ResendAfter60Seconds_IsAllowed()
        {
            var user = SeedUserForPhoneFlow(u =>
            {
                u.PhoneOtpCode = "111111:0912345678";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-60);
            });
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sms.Setup(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((true, string.Empty));

            var (success, _) = await _sut.RequestPhoneVerificationAsync(user.Id, "0912345678");

            success.Should().BeTrue();
            _sms.Verify(s => s.SendSmsAsync("0912345678", It.IsAny<string>()), Times.Once);
        }

        // L1-AUTH-20 | BVA-Max+1 | Vượt hạn mức gửi OTP (lần thứ 6 trong 30 phút) -> chặn
        // 🔴 SPEC GAP v2.2: không tồn tại bộ đếm số lần gửi OTP -> gửi bao nhiêu lần cũng được.
        // FT-01 BV-02 / BR-024 yêu cầu rate limit. Test ĐỎ cho tới khi bổ sung.
        [Fact]
        public async Task L1_AUTH_20_ExceedResendRateLimit_IsBlocked()
        {
            var user = SeedUserForPhoneFlow();
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sms.Setup(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((true, string.Empty));

            for (var i = 0; i < 5; i++)
            {
                user.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90); // đủ 60s giữa các lần
                await _sut.RequestPhoneVerificationAsync(user.Id, "0912345678");
            }
            _sms.Invocations.Clear();

            user.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-90);
            var (success, _) = await _sut.RequestPhoneVerificationAsync(user.Id, "0912345678");

            success.Should().BeFalse("lần thứ 6 trong 30 phút phải bị chặn theo rate limit");
            _sms.Verify(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-AUTH-21 | BVA | OTP đúng trong hạn -> xác thực thành công và là SINGLE-USE
        [Fact]
        public async Task L1_AUTH_21_VerifyPhoneOtp_ValidWithinWindow_IsSingleUse()
        {
            var user = SeedUserForPhoneFlow(u =>
            {
                u.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword("123456")}:0912345678";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddSeconds(1); // còn hạn (tạo cách đây 4'59")
            });

            var first = await _sut.VerifyPhoneOtpAsync(user.Id, "123456", "0912345678");
            var second = await _sut.VerifyPhoneOtpAsync(user.Id, "123456", "0912345678");

            first.Success.Should().BeTrue();
            user.IsPhoneVerified.Should().BeTrue();
            second.Success.Should().BeFalse("OTP chỉ dùng được đúng 1 lần");
        }

        // L1-AUTH-22 | BVA-Max+1 | OTP quá mốc 5 phút -> hết hạn, SĐT KHÔNG đổi
        [Fact]
        public async Task L1_AUTH_22_VerifyPhoneOtp_Expired_IsRejected()
        {
            var oldPhone = "0900000000";
            var user = SeedUserForPhoneFlow(u =>
            {
                u.PhoneNumber = oldPhone;
                u.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword("123456")}:0912345678";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddSeconds(-1); // đã quá mốc 5 phút
            });

            var (success, message) = await _sut.VerifyPhoneOtpAsync(user.Id, "123456", "0912345678");

            success.Should().BeFalse();
            message.Should().Contain("hết hạn");
            user.PhoneNumber.Should().Be(oldPhone, "SĐT không được đổi khi OTP đã hết hạn");
            user.IsPhoneVerified.Should().BeFalse();
        }

        // L1-AUTH-23 | BVA-Max+1 | Sai OTP tới lần thứ 6 -> khoá theo giới hạn số lần thử
        // 🔴 SPEC GAP v2.2: VerifyPhoneOtpAsync không đếm số lần nhập sai -> có thể brute-force 10^6 mã.
        // FT-01 BV-02 / NAC-02 yêu cầu tối đa 5 lần sai. Test ĐỎ cho tới khi bổ sung bộ đếm.
        [Fact]
        public async Task L1_AUTH_23_VerifyPhoneOtp_TooManyWrongAttempts_IsLocked()
        {
            var user = SeedUserForPhoneFlow(u =>
            {
                u.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword("123456")}:0912345678";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(4);
            });

            for (var i = 0; i < 5; i++)
                await _sut.VerifyPhoneOtpAsync(user.Id, "999999", "0912345678");

            // Lần thứ 6 — kể cả nhập ĐÚNG mã cũng phải bị chặn vì đã cạn lượt thử
            var (success, message) = await _sut.VerifyPhoneOtpAsync(user.Id, "123456", "0912345678");

            success.Should().BeFalse("đã vượt giới hạn 5 lần nhập sai");
            user.IsPhoneVerified.Should().BeFalse();
        }

        // L1-AUTH-24 | EP-Valid | SĐT mới CHỈ có hiệu lực SAU khi xác thực thành công
        [Fact]
        public async Task L1_AUTH_24_PhoneNumberChangesOnlyAfterVerification()
        {
            var oldPhone = "0900000000";
            var newPhone = "0912345678";
            var user = SeedUserForPhoneFlow(u => u.PhoneNumber = oldPhone);
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _sms.Setup(s => s.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((true, string.Empty));

            await _sut.RequestPhoneVerificationAsync(user.Id, newPhone);
            user.PhoneNumber.Should().Be(oldPhone, "trước khi xác thực, SĐT vẫn là số CŨ");

            var otpInSms = System.Text.RegularExpressions.Regex.Match(
                (string)_sms.Invocations.Single().Arguments[1]!, @"\d{6}").Value;
            var (success, _) = await _sut.VerifyPhoneOtpAsync(user.Id, otpInSms, newPhone);

            success.Should().BeTrue();
            user.PhoneNumber.Should().Be(newPhone, "sau khi xác thực đúng, SĐT mới thay thế số cũ");
            user.IsPhoneVerified.Should().BeTrue();
        }

        // L1-AUTH-25 | EP-Valid | CompleteProfile sau Google login -> bổ sung thông tin, không tạo tài khoản thứ 2
        [Fact]
        public async Task L1_AUTH_25_CompleteProfile_FillsMissingInfoWithoutCreatingSecondAccount()
        {
            var user = SeedUserForPhoneFlow(u =>
            {
                u.FullName = string.Empty;
                u.PhoneNumber = string.Empty;
                u.GoogleId = "google-sub-123";
            });
            _userRepo.Setup(r => r.PhoneExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var (success, _) = await _sut.CompleteProfileAsync(user.Id, new CompleteProfileDto
            {
                FullName = "Nguyễn Văn A",
                PhoneNumber = "0912345678"
            });

            success.Should().BeTrue();
            user.FullName.Should().Be("Nguyễn Văn A");
            user.PhoneNumber.Should().Be("0912345678");
            _userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never, "không được tạo tài khoản thứ 2");
        }
    }
}
