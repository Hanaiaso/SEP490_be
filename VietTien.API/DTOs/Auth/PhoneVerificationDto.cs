using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class RequestPhoneOtpDto
    {
        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public class VerifyPhoneOtpDto
    {
        [Required(ErrorMessage = "Mã xác minh không được để trống.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã xác minh phải có đúng 6 ký tự.")]
        public string OtpCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
