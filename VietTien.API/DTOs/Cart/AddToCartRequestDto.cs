using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Cart
{
    public class AddToCartRequestDto
    {
        [Required]
        public Guid ProductId { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}
