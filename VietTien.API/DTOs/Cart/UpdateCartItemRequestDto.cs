using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Cart
{
    public class UpdateCartItemRequestDto
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}
