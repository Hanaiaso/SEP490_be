using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class CompleteProfileDto
    {
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>Mật khẩu (tuỳ chọn) — cho phép đăng ký Google đặt mật khẩu để sau này có thể đăng nhập bằng email/mật khẩu</summary>
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        public string? Password { get; set; }

        public string? ConfirmPassword { get; set; }
    }
}
