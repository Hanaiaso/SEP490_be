namespace VietTien.API.Models
{
    // Nhóm C (Group C): chuyến giao hàng theo xe/ca/ngày, tách biệt khỏi luồng lập lịch theo
    // từng Order hiện có (Order.DeliveryVehicleId/DeliveryShift) — không thay thế luồng cũ, chỉ
    // bổ sung một luồng Trip-based mới song song (xem DeliveryTripService).
    public enum DeliveryTripStatus { Scheduled, InDelivery, Completed, Cancelled }

    public class DeliveryTrip
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid VehicleId { get; set; }
        public string Shift { get; set; } = string.Empty; // Sáng / Trưa / Chiều
        public DateTime TripDate { get; set; }

        public DeliveryTripStatus Status { get; set; } = DeliveryTripStatus.Scheduled;

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Navigation Properties
        public Vehicle Vehicle { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<DeliveryAttempt> Attempts { get; set; } = new List<DeliveryAttempt>();
    }
}
