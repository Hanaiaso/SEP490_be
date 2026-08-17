namespace VietTien.API.Models
{
    public class CartItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CartId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } // Hỗ trợ giữ giá 24h

        // Mốc riêng cho từng dòng khi sản phẩm bị 1 đợt ProductPriceUpdateOrder đổi giá khi đang nằm
        // trong giỏ — cho dòng này 1 cửa sổ giữ giá 24h TÍNH TỪ ĐÚNG lúc giá đổi, thay vì phụ thuộc
        // Cart.UpdatedAt (mốc chung cho cả giỏ, đổi mỗi khi khách thao tác bất kỳ gì khác trong giỏ).
        public DateTime? PriceLockedAt { get; set; }

        // Navigation Properties
        public Cart Cart { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
