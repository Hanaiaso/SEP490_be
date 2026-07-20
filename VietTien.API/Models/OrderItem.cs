namespace VietTien.API.Models
{
    public class OrderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }

        // Quy tắc Business Rule: Bắt buộc khóa giá tại thời điểm đặt (Price Snapshot)
        public decimal PriceSnapshot { get; set; }
        public decimal CostSnapshot { get; set; } // Phục vụ hạch toán Lãi gộp (Gross Profit) theo mô hình Cost Component

        public int PackedQuantity { get; set; } = 0; // Số lượng hàng đã được đóng gói
        public string? EvidenceImageUrl { get; set; } // Ảnh bằng chứng lấy hàng (cho riêng từng mặt hàng)

        // Navigation Properties
        public Order Order { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
