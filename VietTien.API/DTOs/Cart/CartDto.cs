namespace VietTien.API.DTOs.Cart
{
    public class CartDto
    {
        public Guid Id { get; set; }
        public Guid CustomerProfileId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<CartItemDto> Items { get; set; } = new List<CartItemDto>();
        public int TotalItems { get; set; }
        public decimal TotalPrice { get; set; }
        // BR-025: true nếu giỏ hàng giữ giá quá 24h — FE phải cảnh báo và chặn thanh toán
        // cho tới khi khách bấm làm mới giá (xem CartService.GetCartAsync/RefreshCartPricesAsync).
        public bool IsPriceExpired { get; set; }
    }

    public class CartItemDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => Quantity * UnitPrice;
    }
}
