namespace VietTien.API.Models
{
    // Nhóm C (Group C): chuyến giao hàng theo xe/ca/ngày, tách biệt khỏi luồng lập lịch theo
    // từng Order hiện có (Order.DeliveryVehicleId/DeliveryShift) — không thay thế luồng cũ, chỉ
    // bổ sung một luồng Trip-based mới song song (xem DeliveryTripService).
    // Loading: nhân viên đang bốc hàng lên xe — trip chỉ được thêm/rút đơn khi ở trạng thái này.
    // StartTripAsync (xuất phát thật) nay yêu cầu Loading thay vì Scheduled.
    // Gán giá trị số tường minh: Loading thêm SAU cùng (=4) để KHÔNG dịch chuyển giá trị int của
    // InDelivery/Completed/Cancelled đã lưu sẵn trong DB cho các dòng DeliveryTrip đã tồn tại trước đó
    // — chèn giữa danh sách (ordinal ngầm định) sẽ âm thầm đổi nghĩa dữ liệu cũ (VD: InDelivery cũ = 1
    // sẽ biến thành Loading nếu chèn Loading vào vị trí thứ 2).
    public enum DeliveryTripStatus { Scheduled = 0, InDelivery = 1, Completed = 2, Cancelled = 3, Loading = 4 }

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

        // Giờ dự kiến (nhân viên nhập khi bắt đầu bốc hàng) — hiển thị cho khách hàng ở OrderDetail.
        public DateTime? PlannedDepartureAt { get; set; }
        public DateTime? PlannedArrivalAt { get; set; }

        // Navigation Properties
        public Vehicle Vehicle { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<DeliveryAttempt> Attempts { get; set; } = new List<DeliveryAttempt>();
    }
}
