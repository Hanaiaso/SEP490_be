using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? TaxCode { get; set; }

        // Mã giới thiệu của Sale (ReferralCode) hoặc email của Sale
        [MaxLength(100)]
        public string? ReferralCode { get; set; }
    }
}
