using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Order
{
    public class ReturnDeliveredOrderRequestDto
    {
        [Required(ErrorMessage = "Vui lòng nhập lý do hoàn hàng.")]
        public string Reason { get; set; } = string.Empty;
    }
}
