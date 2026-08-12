using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-AuthAPI</c> — AUTH-01..13.
    ///
    /// Chuẩn assert: HYBRID (xem tests/L3_ENDPOINT_DRIFT.md). Test bắn vào endpoint THẬT, assert
    /// HTTP status + hành vi nghiệp vụ THẬT. Workbook còn kỳ vọng trường <c>errorCode</c> — code
    /// không có trường này ở bất kỳ đâu; việc đó được chốt MỘT LẦN trong
    /// <see cref="L3ContractDriftTests"/> (defect DEF-L3-001), không lặp lại ở từng case.
    /// </summary>
    public class L3AuthApiTests : L3TestBase
    {
        public L3AuthApiTests(L3SqlFixture factory) : base(factory) { }

        private static object RegisterBody(string email, string phone, string? referral = null) => new
        {
            FullName = "L3 Test User",
            Email = email,
            PhoneNumber = phone,
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd!",
            ReferralCode = referral
        };

        // ── Block: POST /api/auth/register · POST /api/auth/verify-otp ────────────────────────

        /// AUTH-01 | Input-Domain-Happy | FT-01 AC-02; BR-023
        /// Đăng ký email chưa tồn tại -> 2xx, DB có User chờ xác minh, PasswordHash != plaintext.
        [Fact]
        public async Task L3_AUTH_01_Register_UniqueEmail_CreatesPendingUser_WithHashedPassword()
        {
            var client = AnonymousClient();
            var email = NewEmail();

            var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, NewPhone()));

            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var user = await QueryAsync(db => db.Users.SingleAsync(u => u.Email == email));
            user.IsEmailVerified.Should().BeFalse("tài khoản phải ở trạng thái chờ xác minh");
            user.PasswordHash.Should().NotBe("Passw0rd!");
            user.PasswordHash.Should().StartWith("$2", "BCrypt hash luôn bắt đầu bằng $2");
            user.Role.Should().Be(SystemRole.Customer);
        }

        /// AUTH-02 | Input-Domain-Error | FT-01 NAC-01
        /// Email trùng: workbook chờ 409 + DUPLICATE_IDENTITY; code trả 400 + {message}.
        /// Hybrid: quy tắc "không cho trùng danh tính" ĐƯỢC thực thi -> Pass, ghi lệch status/errorCode.
        [Fact]
        public async Task L3_AUTH_02_Register_DuplicateEmail_Rejected_NoSecondUserCreated()
        {
            var client = AnonymousClient();
            var email = NewEmail();

            (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, NewPhone())))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, NewPhone()));

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "code trả 400 chứ không phải 409 như workbook ghi — xem DEF-L3-002");
            (await ReadMessageAsync(res)).Should().Contain("đã được sử dụng");

            (await QueryAsync(db => db.Users.CountAsync(u => u.Email == email)))
                .Should().Be(1, "không được tạo bản ghi thứ 2");
        }

        /// AUTH-03 | Input-Domain-Error | FT-01 NAC-01
        /// Workbook: POST /api/auth/verify-email?token= với token sai/hết hạn.
        /// Thực tế KHÔNG có luồng xác minh bằng token — hệ thống dùng OTP 6 số gửi qua email.
        /// Map sang verify-otp với mã sai: phải bị từ chối và KHÔNG kích hoạt tài khoản.
        [Fact]
        public async Task L3_AUTH_03_VerifyOtp_InvalidCode_Rejected_AccountStaysUnverified()
        {
            var client = AnonymousClient();
            var email = NewEmail();
            await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, NewPhone()));

            var res = await client.PostAsJsonAsync("/api/auth/verify-otp",
                new { Email = email, OtpCode = "000000" });

            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await QueryAsync(db => db.Users.SingleAsync(u => u.Email == email)))
                .IsEmailVerified.Should().BeFalse();
        }

        /// AUTH-04 | BVA | FT-01 BV-02; NAC-02; BR-024
        /// Biên độ dài OTP 5 / 6 / 7 chữ số. Chỉ đúng 6 chữ số mới qua được luật độ dài
        /// ([StringLength(6, MinimumLength = 6)] trên VerifyOtpDto).
        [Theory]
        [InlineData("12345", false)]   // 5 chữ số
        [InlineData("123456", true)]   // 6 chữ số
        [InlineData("1234567", false)] // 7 chữ số
        public async Task L3_AUTH_04_VerifyOtp_LengthBoundary_Only6DigitsPassesValidation(
            string otp, bool passesLengthRule)
        {
            var client = AnonymousClient();
            var email = NewEmail();
            await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, NewPhone()));

            var res = await client.PostAsJsonAsync("/api/auth/verify-otp", new { Email = email, OtpCode = otp });
            var body = await res.Content.ReadAsStringAsync();

            // Cả 3 nhánh đều 400 (nhánh 6 số vì mã không khớp), nhưng LÝ DO phải khác nhau:
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            if (passesLengthRule)
                body.Should().Contain("OTP không chính xác",
                    "6 chữ số phải qua được luật độ dài, chỉ trượt ở bước so mã");
            else
                body.Should().Contain("6 ký tự", "5 và 7 chữ số phải bị chặn bởi luật độ dài");
        }

        /// AUTH-05 | BVA | FT-01 BV-02; NAC-02
        /// Biên hạn OTP 5 phút: gửi tại T+4:59 -> chấp nhận; tại T+5:00 -> hết hạn.
        /// Không lùi được đồng hồ hệ thống nên seed thẳng OtpExpiry (AuthService.cs:85 đặt = now + 5').
        [Theory]
        [InlineData(299, true)]  // OTP tạo cách đây 4:59 -> còn hiệu lực
        [InlineData(300, false)] // OTP tạo cách đây 5:00 -> vừa hết hạn
        public async Task L3_AUTH_05_VerifyOtp_ExpiryBoundary_5Minutes(int otpAgeSeconds, bool shouldSucceed)
        {
            var client = AnonymousClient();
            var email = NewEmail();
            const string otp = "654321";

            await SeedAsync(db =>
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "OTP Boundary",
                    Email = email,
                    PhoneNumber = NewPhone(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Passw0rd!"),
                    Role = SystemRole.Customer,
                    IsActive = true,
                    IsEmailVerified = false,
                    OtpCode = otp,
                    OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-otpAgeSeconds),
                });
                return Task.CompletedTask;
            });

            var res = await client.PostAsJsonAsync("/api/auth/verify-otp", new { Email = email, OtpCode = otp });

            if (shouldSucceed)
            {
                res.StatusCode.Should().Be(HttpStatusCode.OK, "tại 4:59 OTP vẫn còn hiệu lực");
            }
            else
            {
                res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await ReadMessageAsync(res)).Should().Contain("hết hạn");
            }
        }

        /// AUTH-06 | BVA | FT-01 BV-02; NAC-02
        /// Giới hạn số lần nhập sai. Workbook ghi verify-otp + "Bearer JWT" -> luồng OTP ĐIỆN THOẠI
        /// (POST /api/auth/verify-phone-otp, [Authorize]) vì CHỈ luồng này có bộ đếm
        /// PhoneOtpFailedAttempts (max 5). Luồng OTP email KHÔNG có bộ đếm nào — ghi trong DEF-L3-007.
        [Fact]
        public async Task L3_AUTH_06_VerifyPhoneOtp_AttemptLimit_BlocksAt6thTry()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer, u => u.IsPhoneVerified = false);
            var phone = NewPhone();

            await SeedAsync(async db =>
            {
                var u = await db.Users.SingleAsync(x => x.Id == user.Id);
                u.PhoneOtpCode = $"{BCrypt.Net.BCrypt.HashPassword("111111")}:{phone}";
                u.PhoneOtpExpiry = DateTime.UtcNow.AddMinutes(5);
                u.PhoneOtpFailedAttempts = 0;
            });

            // 5 lần sai đầu: bị từ chối vì SAI MÃ.
            for (var i = 1; i <= 5; i++)
            {
                var wrong = await client.PostAsJsonAsync("/api/auth/verify-phone-otp",
                    new { OtpCode = "999999", PhoneNumber = phone });
                wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"lần thử sai thứ {i}");
            }

            // Lần thứ 6: bị chặn bởi GIỚI HẠN, kể cả khi gửi mã ĐÚNG.
            var sixth = await client.PostAsJsonAsync("/api/auth/verify-phone-otp",
                new { OtpCode = "111111", PhoneNumber = phone });

            sixth.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "code trả 400 chứ không phải 409 như workbook ghi — xem DEF-L3-002");

            (await QueryAsync(db => db.Users.SingleAsync(u => u.Id == user.Id)))
                .IsPhoneVerified.Should().BeFalse("mã ĐÚNG ở lần thứ 6 vẫn KHÔNG được chấp nhận");
        }

        /// AUTH-07 | BVA | FT-01 BV-02; NAC-02
        /// Cooldown gửi lại OTP 60 giây: tại 59s bị chặn, sau 60s cho gửi.
        /// Code suy lastSentAt = OtpExpiry - 5' (AuthService.cs:178) nên seed OtpExpiry là đủ.
        [Theory]
        [InlineData(59, false)] // vừa gửi cách đây 59s -> chặn
        [InlineData(61, true)]  // đã qua 60s -> cho gửi
        public async Task L3_AUTH_07_ResendOtp_CooldownBoundary_60Seconds(
            int secondsSinceLastSend, bool shouldSucceed)
        {
            var client = AnonymousClient();
            var email = NewEmail();

            await SeedAsync(db =>
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Resend Cooldown",
                    Email = email,
                    PhoneNumber = NewPhone(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Passw0rd!"),
                    Role = SystemRole.Customer,
                    IsActive = true,
                    IsEmailVerified = false,
                    OtpCode = "222222",
                    OtpExpiry = DateTime.UtcNow.AddMinutes(5).AddSeconds(-secondsSinceLastSend),
                });
                return Task.CompletedTask;
            });

            var res = await client.PostAsJsonAsync("/api/auth/resend-otp", new { Email = email });

            if (shouldSucceed)
            {
                res.StatusCode.Should().Be(HttpStatusCode.OK, "qua 60s thì được gửi lại");
            }
            else
            {
                res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                    "code trả 400 chứ không phải 429 như workbook ghi — xem DEF-L3-002");
                (await ReadMessageAsync(res)).Should().Contain("60 giây");
            }
        }

        // ── Block: login · refresh · logout ───────────────────────────────────────────────────

        /// AUTH-08 | Input-Domain-Happy | NFR-SEC02; NFR-SEC03
        [Fact]
        public async Task L3_AUTH_08_Login_ValidCredentials_ReturnsTokensAndRole()
        {
            var client = AnonymousClient();

            var res = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = L3Seed.CustomerEmail, Password = L3Seed.DefaultPassword });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var data = (await ReadJsonAsync(res)).GetProperty("data");
            data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
            data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
            data.GetProperty("user").GetProperty("role").GetString().Should().Be("Customer");
        }

        /// AUTH-09 | Input-Domain-Error | NFR-SEC02
        /// Sai mật khẩu -> thông điệp CHUNG, không lộ trường nào sai (chống user enumeration).
        [Fact]
        public async Task L3_AUTH_09_Login_WrongPassword_GenericMessage_SameAsUnknownEmail()
        {
            var client = AnonymousClient();

            var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = L3Seed.CustomerEmail, Password = "SaiMatKhau!" });
            var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = NewEmail(), Password = "SaiMatKhau!" });

            wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var m1 = await ReadMessageAsync(wrongPassword);
            var m2 = await ReadMessageAsync(unknownEmail);
            m1.Should().Be(m2,
                "hai nhánh phải trả CÙNG thông điệp; nếu khác là lộ email có tồn tại trong hệ thống hay không");
        }

        /// AUTH-10 | Input-Domain-Happy | NFR-SEC02
        /// Refresh hợp lệ -> access token mới VÀ refresh token MỚI (rotation).
        /// Workbook ghi POST /api/auth/refresh; endpoint thật là /api/auth/refresh-token.
        [Fact]
        public async Task L3_AUTH_10_RefreshToken_Valid_RotatesRefreshToken()
        {
            var client = AnonymousClient();
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = L3Seed.CustomerEmail, Password = L3Seed.DefaultPassword });
            var rt1 = (await ReadJsonAsync(login)).GetProperty("data").GetProperty("refreshToken").GetString()!;

            var res = await client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = rt1 });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var data = (await ReadJsonAsync(res)).GetProperty("data");
            data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
            data.GetProperty("refreshToken").GetString().Should().NotBe(rt1, "refresh token phải được xoay vòng");
        }

        /// AUTH-11 | Input-Domain-Error | NFR-SEC02
        /// Dùng lại refresh token đã bị xoay -> 401.
        [Fact]
        public async Task L3_AUTH_11_RefreshToken_ReusedAfterRotation_Unauthorized()
        {
            var client = AnonymousClient();
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = L3Seed.CustomerEmail, Password = L3Seed.DefaultPassword });
            var rt1 = (await ReadJsonAsync(login)).GetProperty("data").GetProperty("refreshToken").GetString()!;

            (await client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = rt1 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var reuse = await client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = rt1 });

            reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "token cũ phải bị vô hiệu sau rotation");
        }

        /// AUTH-12 | Input-Domain-Error | NFR-SEC03
        /// Không gửi Authorization tới endpoint đã bảo vệ -> 401, không trả dữ liệu nghiệp vụ.
        /// Workbook ghi GET /api/orders — route trần đó không tồn tại; dùng /api/orders/my-history.
        [Fact]
        public async Task L3_AUTH_12_ProtectedEndpoint_NoAuthorizationHeader_Unauthorized()
        {
            var res = await AnonymousClient().GetAsync("/api/orders/my-history");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await res.Content.ReadAsStringAsync()).Should().NotContain("orderCode");
        }

        /// AUTH-13 | Input-Domain-Error | NFR-SEC03
        /// JWT hết hạn -> 401, không trả dữ liệu nghiệp vụ.
        [Fact]
        public async Task L3_AUTH_13_ProtectedEndpoint_ExpiredJwt_Unauthorized()
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateExpiredJwt());

            var res = await client.GetAsync("/api/orders/my-history");

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await res.Content.ReadAsStringAsync()).Should().NotContain("orderCode");
        }
    }
}
