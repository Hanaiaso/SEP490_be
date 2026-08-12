namespace VietTien.API.Models
{
    public enum DeliveryConflictStatus
    {
        Pending,
        Resolved,
        Rejected
    }

    // UC-34: ghi nhận 1 lần lập lịch xe/ca bị trùng (đã có chuyến khác Scheduled/InDelivery
    // cùng xe + ca + ngày) để Sales Manager chủ động xử lý, thay vì chỉ chặn cứng bằng lỗi 409
    // như trước — xem OrderService.ScheduleDeliveryAsync.
    public class DeliveryScheduleConflict
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public int VehicleId { get; set; }
        public string Shift { get; set; } = string.Empty; // Sáng / Trưa / Chiều
        public DateTime RequestedDate { get; set; }

        // Danh sách Guid các Order (hoặc StockTransfer) bị chặn, lưu dạng chuỗi phân tách bởi dấu phẩy
        // — số lượng nhỏ trong 1 lần lập lịch nên không cần bảng join riêng.
        public string OrderIds { get; set; } = string.Empty;

        public DeliveryConflictStatus Status { get; set; } = DeliveryConflictStatus.Pending;

        public Guid RaisedByUserId { get; set; }
        public DateTime RaisedAt { get; set; } = DateTime.UtcNow;

        public Guid? ResolvedByUserId { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionAction { get; set; } // Reassign | Override | Reject
        public string? ResolutionNote { get; set; }
    }
}
