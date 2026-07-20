using VietTien.API.DTOs.Cart;

namespace VietTien.API.DTOs.Order
{
    public class OrderPreviewDto
    {
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal VatPercentage { get; set; }
        public decimal VatAmount { get; set; }
        public decimal FinalPayment { get; set; }
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
    }
}
