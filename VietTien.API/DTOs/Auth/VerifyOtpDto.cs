using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class VerifyOtpDto
    {
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã OTP không được để trống.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải đúng 6 ký tự.")]
        public string OtpCode { get; set; } = string.Empty;
    }
}
