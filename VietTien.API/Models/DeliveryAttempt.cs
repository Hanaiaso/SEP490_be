namespace VietTien.API.Models
{
    // Lịch sử từng lần giao thử của một đơn trong 1 DeliveryTrip — Order.FailedDeliveryCount chỉ là
    // bộ đếm, không có bằng chứng theo từng lần; bảng này bổ sung 1 dòng/lần giao (thành công hoặc thất bại).
    public enum DeliveryAttemptOutcome { Delivered, Failed }

    public class DeliveryAttempt
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OrderId { get; set; }
        public Guid DeliveryTripId { get; set; }
        public int AttemptNumber { get; set; }

        public DeliveryAttemptOutcome Outcome { get; set; }
        public string? PhotoUrl { get; set; }
        public string? SignatureUrl { get; set; }
        public string? FailureReason { get; set; }

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
        public Guid RecordedByUserId { get; set; }

        // Navigation Properties
        public Order Order { get; set; } = null!;
        public DeliveryTrip DeliveryTrip { get; set; } = null!;
        public User RecordedByUser { get; set; } = null!;
    }
}
