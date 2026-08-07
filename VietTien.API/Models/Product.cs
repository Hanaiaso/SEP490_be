namespace VietTien.API.Models
{
    public class Product
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CategoryId { get; set; }
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

        // Navigation Properties
        public Category Category { get; set; } = null!;
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<AiMarketingCampaign> MarketingCampaigns { get; set; } = new List<AiMarketingCampaign>();
        public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
    }
}
