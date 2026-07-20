using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class RefreshTokenDto
    {
        [Required(ErrorMessage = "Refresh token không được để trống.")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
