using System.ComponentModel.DataAnnotations;
using VietTien.API.Infrastructure.Validation;

namespace VietTien.API.DTOs.Order
{
    public class UploadPdfRequestDto
    {
        [Required(ErrorMessage = "PdfBase64 là bắt buộc.")]
        [Base64Pdf(10)]
        public string PdfBase64 { get; set; } = string.Empty;
    }
}
