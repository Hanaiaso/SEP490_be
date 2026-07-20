namespace VietTien.API.Models
{
    /// <summary>
    /// Theo dõi các trường hợp ngoại lệ: Payment đã PAID nhưng không phân bổ được tồn kho.
    /// PaymentStatus luôn giữ PAID — lỗi allocation là ngoại lệ riêng biệt.
    /// </summary>
    public class PaymentException
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }

        /// <summary>
        /// Mã lý do ngoại lệ:
        /// RESERVATION_EXPIRED — Hết thời gian giữ tồn
        /// INSUFFICIENT_AVAILABLE_STOCK — Không đủ tồn khả dụng
        /// WEBHOOK_NOT_RECEIVED — Webhook không đến
        /// WEBHOOK_AMOUNT_MISMATCH — Số tiền webhook sai
        /// </summary>
        public string ReasonCode { get; set; } = string.Empty;

        /// <summary>Mô tả chi tiết lý do ngoại lệ</summary>
        public string? Description { get; set; }

        /// <summary>Trạng thái xử lý: OPEN | RESOLVED</summary>
        public string Status { get; set; } = "OPEN";

        /// <summary>Số lần đã thử retry allocation</summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>Lần retry gần nhất</summary>
        public DateTime? LastRetryAt { get; set; }

        /// <summary>Sales Manager thực hiện lần retry gần nhất</summary>
        public Guid? LastRetryByUserId { get; set; }

        /// <summary>Thời điểm giải quyết xong ngoại lệ</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>Người giải quyết ngoại lệ</summary>
        public Guid? ResolvedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Order Order { get; set; } = null!;
        public User? LastRetryBy { get; set; }
        public User? ResolvedBy { get; set; }
    }
}
