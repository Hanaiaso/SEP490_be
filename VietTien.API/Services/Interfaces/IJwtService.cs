using System.Security.Claims;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IJwtService
    {
        /// <summary>Tạo Access Token JWT từ thông tin User</summary>
        string GenerateAccessToken(User user);

        /// <summary>Tạo Refresh Token ngẫu nhiên an toàn</summary>
        string GenerateRefreshToken();

        /// <summary>Giải mã Access Token đã hết hạn để lấy Claims</summary>
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);

        /// <summary>Lấy thời điểm hết hạn của Access Token</summary>
        DateTime GetAccessTokenExpiry();
    }
}
