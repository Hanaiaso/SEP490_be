using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public class Product
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CategoryId { get; set; }

        // Concurrency token: chặn 2 CEO/Admin cùng sửa 1 sản phẩm ghi đè lẫn nhau âm thầm ->
        // throw DbUpdateConcurrencyException, middleware map sẵn 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal StandardListedPrice { get; set; }
        public string? Description { get; set; }
        public string? Specifications { get; set; }
        public string? ImageUrl { get; set; }
        public string Unit { get; set; } = "Cái"; // e.g., Cái, Hộp, Cuộn, Kg

        // Quy tắc Business Rule: Soft Delete để bảo toàn toàn vẹn dữ liệu
        public bool IsDiscontinued { get; set; } = false;

        // Đánh giá sản phẩm — denormalized để trang danh sách không phải tính lại từ N review mỗi lần load
        public double AverageRating { get; set; } = 0;
        public int ReviewCount { get; set; } = 0;

        // Ngưỡng cảnh báo tồn kho (CEO cấu hình ở trang Quản lý sản phẩm) — tính trên TỔNG khả dụng
        // across mọi kho (mirror Material.SafetyThreshold), null = chưa cấu hình, không cảnh báo.
        public int? ReorderThreshold { get; set; }   // dưới ngưỡng này = tồn thấp
        public int? ExcessThreshold { get; set; }    // vượt ngưỡng này = tồn đọng
        public DateTime? LastAlertSentDate { get; set; } // cooldown dùng chung cho cả 2 loại cảnh báo trên

        // Navigation Properties
        public Category Category { get; set; } = null!;
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<AiMarketingCampaign> MarketingCampaigns { get; set; } = new List<AiMarketingCampaign>();
        public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
        public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    }
}
