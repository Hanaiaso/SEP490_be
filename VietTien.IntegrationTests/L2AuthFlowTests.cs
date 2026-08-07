using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Auth;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-AuthFlow — register → OTP → login → refresh rotation → logout, trên SQL Server thật.
    ///
    /// OTP KHÔNG cần smtp4dev: AuthService gửi qua IEmailService (AuthService.cs:101), mà hạ tầng L2 đã
    /// thay bằng FakeEmailService, nên đọc thẳng mã từ Factory.Email.Sent — nhanh hơn và tất định hơn
    /// việc dựng thêm một container mail-sink (vốn còn vướng StartTls ở EmailService.cs:366,390).
    /// </summary>
    [Trait("Category", "L2")]
    public class L2AuthFlowTests : SqlServerTestBase
    {
        public L2AuthFlowTests(SqlServerFixture factory) : base(factory) { }

        private const string GoodPassword = "P@ss123456";

        private static string UniqueEmail() => $"auth.{Guid.NewGuid():N}@test.local";
        private static string UniquePhone() => "09" + Random.Shared.Next(0, 100_000_000).ToString("D8");

        /// <summary>Seed 1 user đã xác minh với mật khẩu BCrypt biết trước, để test login/refresh.</summary>
        private async Task<(Guid Id, string Email)> SeedVerifiedUserAsync()
        {
            var id = Guid.NewGuid();
            var email = UniqueEmail();
            await SeedAsync(async db =>
            {
                db.Users.Add(new User
                {
                    Id = id,
                    FullName = "Auth Test User",
                    Email = email,
                    PhoneNumber = UniquePhone(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(GoodPassword),
                    Role = SystemRole.Customer,
                    IsActive = true,
                    IsEmailVerified = true
                });
                await Task.CompletedTask;
            });
            return (id, email);
        }

        private static async Task<string?> ReadTokenAsync(HttpResponseMessage response, string propertyName)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals(propertyName)) return prop.Value.GetString();
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    foreach (var inner in prop.Value.EnumerateObject())
                        if (inner.NameEquals(propertyName)) return inner.Value.GetString();
            }
            return null;
        }

        private Task<User> ReloadUserAsync(Guid id) =>
            QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == id));

        // ── L2-AUTH-01 ─────────────────────────────────────────────────────────────────────

        // GIVEN  Chưa có user nào với email này
        // WHEN   register → đọc OTP → verify-otp → login
        // THEN   DB lưu BCrypt hash (khác plaintext); IsEmailVerified=true sau verify;
        //        login trả access + refresh token và refresh được lưu xuống DB
        [Fact]
        [Trait("TestID", "L2-AUTH-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-01 AC-02; AC-04; BR-023; BR-024; NFR-SEC04")]
        public async Task L2_AUTH_01_RegisterVerifyLoginEndToEnd()
        {
            await ResetAsync();
            var client = Factory.CreateClient();
            var email = UniqueEmail();

            // 1) Đăng ký
            var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
            {
                FullName = "Nguoi Dung Moi",
                Email = email,
                PhoneNumber = UniquePhone(),
                Password = GoodPassword,
                ConfirmPassword = GoodPassword
            });
            register.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await register.Content.ReadAsStringAsync());

            // (b) DB — mật khẩu phải được băm, tuyệt đối không lưu plaintext
            var created = await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == email));
            created.PasswordHash.Should().NotBe(GoodPassword, "NFR-SEC04: không được lưu mật khẩu thô");
            BCrypt.Net.BCrypt.Verify(GoodPassword, created.PasswordHash).Should().BeTrue("phải là BCrypt hash hợp lệ");
            created.IsEmailVerified.Should().BeFalse("chưa xác minh OTP");

            // (c) side effect — OTP được gửi qua IEmailService
            var otpMail = Factory.Email.Sent.LastOrDefault(m => m.To == email && m.Subject == "OTP");
            otpMail.Should().NotBeNull("phải gửi email OTP khi đăng ký");
            var otpCode = otpMail!.Body;
            otpCode.Should().NotBeNullOrWhiteSpace();

            // 2) Xác minh OTP
            var verify = await client.PostAsJsonAsync("/api/auth/verify-otp", new VerifyOtpDto
            {
                Email = email,
                OtpCode = otpCode
            });
            verify.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await verify.Content.ReadAsStringAsync());

            (await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == email)))
                .IsEmailVerified.Should().BeTrue("sau verify-otp phải được đánh dấu đã xác minh");

            // 3) Đăng nhập
            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
            {
                Email = email,
                Password = GoodPassword
            });
            login.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await login.Content.ReadAsStringAsync());

            var accessToken = await ReadTokenAsync(login, "accessToken");
            var refreshToken = await ReadTokenAsync(login, "refreshToken");
            accessToken.Should().NotBeNullOrWhiteSpace();
            refreshToken.Should().NotBeNullOrWhiteSpace();

            // (b) refresh token phải được lưu xuống DB, không chỉ trả về cho client
            var afterLogin = await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == email));
            afterLogin.RefreshToken.Should().Be(refreshToken);
            afterLogin.RefreshTokenExpiryTime.Should().NotBeNull();
            afterLogin.RefreshTokenExpiryTime!.Value.Should().BeAfter(DateTime.UtcNow);
        }

        // ── L2-AUTH-02 ─────────────────────────────────────────────────────────────────────

        // GIVEN  User đã xác minh, mật khẩu đúng là GoodPassword
        // WHEN   POST /api/auth/login với mật khẩu SAI
        // THEN   Bị từ chối, body không chứa token; DB không set refresh token
        [Fact]
        [Trait("TestID", "L2-AUTH-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "NFR-SEC02")]
        public async Task L2_AUTH_02_LoginWithWrongPasswordIsRejected()
        {
            await ResetAsync();
            var (userId, email) = await SeedVerifiedUserAsync();

            var response = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginDto
            {
                Email = email,
                Password = "SAI-HOAN-TOAN"
            });

            // (a) HTTP
            response.IsSuccessStatusCode.Should().BeFalse("mật khẩu sai không được đăng nhập");
            var body = await response.Content.ReadAsStringAsync();
            (await ReadTokenAsync(response, "accessToken")).Should().BeNullOrEmpty("body không được chứa token");
            (await ReadTokenAsync(response, "refreshToken")).Should().BeNullOrEmpty();
            body.Should().NotContain(GoodPassword);

            // (b) DB
            var user = await ReloadUserAsync(userId);
            user.RefreshToken.Should().BeNull("đăng nhập thất bại không được cấp refresh token");
            user.RefreshTokenExpiryTime.Should().BeNull();
        }

        // ── L2-AUTH-03 ─────────────────────────────────────────────────────────────────────

        // GIVEN  User đã đăng nhập, DB đang giữ refresh token RT1
        // WHEN   refresh-token{RT1}; rồi refresh-token{RT1} LẦN NỮA
        // THEN   Lần 1 trả access mới + RT2, DB lưu RT2; lần 2 với RT1 cũ bị từ chối (rotation)
        [Fact]
        [Trait("TestID", "L2-AUTH-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "NFR-SEC02")]
        public async Task L2_AUTH_03_RefreshTokenRotationInvalidatesOldToken()
        {
            await ResetAsync();
            var (userId, email) = await SeedVerifiedUserAsync();
            var client = Factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login",
                new LoginDto { Email = email, Password = GoodPassword });
            login.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await login.Content.ReadAsStringAsync());
            var rt1 = await ReadTokenAsync(login, "refreshToken");
            rt1.Should().NotBeNullOrWhiteSpace();

            // Lần 1 — phải thành công và xoay token
            var first = await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenDto { RefreshToken = rt1! });
            first.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await first.Content.ReadAsStringAsync());

            var rt2 = await ReadTokenAsync(first, "refreshToken");
            rt2.Should().NotBeNullOrWhiteSpace();
            rt2.Should().NotBe(rt1, "NFR-SEC02: mỗi lần refresh phải xoay sang token mới");
            (await ReadTokenAsync(first, "accessToken")).Should().NotBeNullOrWhiteSpace();

            // (b) DB đang giữ RT2
            (await ReloadUserAsync(userId)).RefreshToken.Should().Be(rt2);

            // Lần 2 với RT1 cũ — phải bị từ chối
            var replay = await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenDto { RefreshToken = rt1! });
            replay.IsSuccessStatusCode.Should().BeFalse(
                "token cũ phải mất hiệu lực ngay sau khi xoay; body: {0}", await replay.Content.ReadAsStringAsync());

            // (b) DB không được bị RT1 ghi đè ngược
            (await ReloadUserAsync(userId)).RefreshToken.Should().Be(rt2, "dùng lại token cũ không được đổi trạng thái");
        }

        // ── L2-AUTH-04 ─────────────────────────────────────────────────────────────────────

        // GIVEN  User đã đăng nhập, DB đang giữ refresh token RT1
        // WHEN   POST /api/auth/logout; rồi POST /api/auth/refresh-token {RT1}
        // THEN   Logout xoá refresh token trong DB; refresh sau logout bị từ chối
        [Fact]
        [Trait("TestID", "L2-AUTH-04")]
        [Trait("Priority", "P2")]
        [Trait("SRSRef", "NFR-SEC02")]
        public async Task L2_AUTH_04_LogoutClearsRefreshTokenAndBlocksRefresh()
        {
            await ResetAsync();
            var (userId, email) = await SeedVerifiedUserAsync();
            var client = Factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login",
                new LoginDto { Email = email, Password = GoodPassword });
            login.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await login.Content.ReadAsStringAsync());

            var accessToken = await ReadTokenAsync(login, "accessToken");
            var rt1 = await ReadTokenAsync(login, "refreshToken");
            rt1.Should().NotBeNullOrWhiteSpace();
            (await ReloadUserAsync(userId)).RefreshToken.Should().Be(rt1);

            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var logout = await client.PostAsync("/api/auth/logout", null);
            logout.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await logout.Content.ReadAsStringAsync());

            // (b) DB — logout phải xoá dấu vết refresh token
            var afterLogout = await ReloadUserAsync(userId);
            afterLogout.RefreshToken.Should().BeNull();
            afterLogout.RefreshTokenExpiryTime.Should().BeNull();

            // Refresh sau logout phải bị từ chối
            var refresh = await Factory.CreateClient()
                .PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenDto { RefreshToken = rt1! });
            refresh.IsSuccessStatusCode.Should().BeFalse(
                "đã logout thì refresh token cũ không dùng lại được; body: {0}",
                await refresh.Content.ReadAsStringAsync());
        }
    }
}
