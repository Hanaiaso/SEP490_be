using VietTien.API.Models;

namespace VietTien.API.DTOs.Auth
{
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserInfoDto User { get; set; } = null!;

        /// <summary>True nếu đây là lần đăng ký đầu tiên qua Google — frontend sẽ redirect sang trang hoàn thiện hồ sơ</summary>
        public bool IsNewUser { get; set; } = false;
    }

    public class UserInfoDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsEmailVerified { get; set; }

        /// <summary>True nếu hồ sơ đã đầy đủ (có ít nhất 1 địa chỉ giao hàng) — đủ điều kiện mua hàng.
        /// Frontend dùng cờ này để điều hướng user mới sang trang cập nhật địa chỉ.</summary>
        public bool IsProfileCompleted { get; set; }

        public static UserInfoDto FromUser(User user) => new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            IsEmailVerified = user.IsEmailVerified
        };
    }
}
