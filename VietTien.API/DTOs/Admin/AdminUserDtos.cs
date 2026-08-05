using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Admin
{
    // Các role mà Admin được phép gán qua RBAC (không bao gồm Customer/Guest —
    // 2 role đó được tạo qua luồng đăng ký công khai, có ràng buộc CustomerProfile riêng).
    public static class AdminAssignableRoles
    {
        public static readonly string[] Values =
        {
            "SalesStaff", "SalesManager", "WarehouseStaff", "AccountingStaff", "CEO", "Admin"
        };
    }

    public class AdminUserQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SearchQuery { get; set; } // tìm theo tên/email/sđt
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
    }

    public class AdminUserDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsEmailVerified { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateStaffUserRequest
    {
        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [MaxLength(200, ErrorMessage = "Họ tên không được vượt quá 200 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vai trò là bắt buộc.")]
        public string Role { get; set; } = string.Empty;
    }

    public class ChangeUserRoleRequest
    {
        [Required(ErrorMessage = "Vai trò mới là bắt buộc.")]
        public string NewRole { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập lý do đổi vai trò.")]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }

    public class SetUserActiveStatusRequest
    {
        public bool IsActive { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập lý do.")]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
