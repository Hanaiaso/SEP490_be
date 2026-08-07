namespace VietTien.API.Models
{
    /// <summary>Đánh giá sản phẩm của khách hàng. Chỉ tạo được khi có đơn hàng Completed chứa sản phẩm (verified purchase).</summary>
    public class ProductReview
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Guid CustomerProfileId { get; set; }

        /// <summary>Đơn hàng Completed chứng minh đã mua — snapshot tại thời điểm tạo, không đổi sau đó.</summary>
        public Guid OrderId { get; set; }

        public int Rating { get; set; } // 1-5
        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Phản hồi công khai từ Sales phụ trách/Admin (1 phản hồi/đánh giá)
        public string? ReplyText { get; set; }
        public Guid? RepliedByUserId { get; set; }
        public DateTime? RepliedAt { get; set; }

        // Navigation Properties
        public Product Product { get; set; } = null!;
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public Order Order { get; set; } = null!;
        public User? RepliedByUser { get; set; }
    }
}
