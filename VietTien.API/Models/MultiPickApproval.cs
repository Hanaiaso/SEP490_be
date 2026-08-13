namespace VietTien.API.Models
{
    // FUL-08: gộp pick nhiều đơn cùng lúc phải được Sales Manager duyệt trước (propose -> approve),
    // cùng shape với SalesChangeRequest/StockAdjustment — WarehouseStaff đề xuất, SalesManager quyết định.
    public enum MultiPickApprovalStatus { Pending, Approved, Rejected }

    public class MultiPickApproval
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Danh sách Guid các Order gộp pick, lưu dạng chuỗi phân tách bởi dấu phẩy — cùng convention
        // với DeliveryScheduleConflict.OrderIds (số lượng nhỏ trong 1 lần đề xuất, không cần bảng join riêng).
        public string OrderIds { get; set; } = string.Empty;

        public MultiPickApprovalStatus Status { get; set; } = MultiPickApprovalStatus.Pending;

        public Guid RequestedByUserId { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public Guid? DecidedByUserId { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
    }
}
