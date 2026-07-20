using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Order
{
    public class RejectOrderRequestDto
    {
        [Required(ErrorMessage = "Lý do từ chối là bắt buộc.")]
        public string Reason { get; set; } = string.Empty;
    }
}
