using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>Sheet: JwtService — L1-JWT-01..05</summary>
    public class JwtServiceTests
    {
        private readonly JwtService _sut = new(TestConfig.JwtOptions(accessMinutes: 15));

        private static User NewUser() => TestData.User(u => u.Role = SystemRole.Customer);

        // L1-JWT-01 | EP-Valid | Token chứa đúng userId + role claim, issuer/audience khớp settings
        [Fact]
        public void L1_JWT_01_GenerateAccessToken_ContainsUserIdAndRoleClaims()
        {
            var user = NewUser();

            var tokenString = _sut.GenerateAccessToken(user);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
            token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
            token.Claims.Should().Contain(c => c.Value == "Customer");
            token.Issuer.Should().Be("VietTien.Tests");
            token.Audiences.Should().Contain("VietTien.Tests.Audience");

            // Chữ ký hợp lệ: chính service đọc lại được principal
            _sut.GetPrincipalFromExpiredToken(tokenString).Should().NotBeNull();
        }

        // L1-JWT-02 | EP-Valid | Hạn access token = NOW + AccessTokenExpiryMinutes (15')
        [Fact]
        public void L1_JWT_02_GetAccessTokenExpiry_MatchesConfiguredLifetime()
        {
            var expiry = _sut.GetAccessTokenExpiry();

            expiry.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
        }

        // L1-JWT-03 | EP-Valid | 2 refresh token liên tiếp khác nhau và đủ dài (64 byte random)
        [Fact]
        public void L1_JWT_03_GenerateRefreshToken_DistinctAndCryptographicallySized()
        {
            var a = _sut.GenerateRefreshToken();
            var b = _sut.GenerateRefreshToken();

            a.Should().NotBe(b);
            Convert.FromBase64String(a).Length.Should().BeGreaterThanOrEqualTo(32);
            Convert.FromBase64String(b).Length.Should().BeGreaterThanOrEqualTo(32);
        }

        // L1-JWT-04 | EP-Valid | Token hết hạn nhưng đúng chữ ký -> vẫn lấy được principal (ValidateLifetime = false)
        [Fact]
        public void L1_JWT_04_GetPrincipalFromExpiredToken_ExpiredButValidSignature_ReturnsPrincipal()
        {
            var user = NewUser();
            var expiredService = new JwtService(TestConfig.JwtOptions(accessMinutes: -10)); // token đã hết hạn 10'
            var expiredToken = expiredService.GenerateAccessToken(user);

            var principal = _sut.GetPrincipalFromExpiredToken(expiredToken);

            principal.Should().NotBeNull();
            principal!.Claims.Should().Contain(c => c.Value == user.Id.ToString());
        }

        // L1-JWT-05 | EP-Invalid | Token ký bằng key KHÁC -> trả về null
        [Fact]
        public void L1_JWT_05_GetPrincipalFromExpiredToken_ForgedKey_ReturnsNull()
        {
            var user = NewUser();
            var attackerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("attacker-key-attacker-key-attacker-key-12345"));
            var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: "VietTien.Tests",
                audience: "VietTien.Tests.Audience",
                claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()) },
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: new SigningCredentials(attackerKey, SecurityAlgorithms.HmacSha256)));

            var principal = _sut.GetPrincipalFromExpiredToken(forged);

            principal.Should().BeNull();
        }
    }
}
