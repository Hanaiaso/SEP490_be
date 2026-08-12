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
        // UC-13: true nếu đây là đơn hàng đầu tiên của khách và SĐT chưa được xác thực OTP —
        // FE phải bắt khách hoàn tất xác thực OTP trước khi cho bấm "Đặt hàng".
        public bool RequiresPhoneOtp { get; set; }
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
    }
}
