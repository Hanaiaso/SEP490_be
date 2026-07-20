using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Auth
{
    public class GoogleLoginDto
    {
        [Required(ErrorMessage = "Google ID Token không được để trống.")]
        public string IdToken { get; set; } = string.Empty;
    }
}
