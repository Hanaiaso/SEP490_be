using System;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    // Trạng thái đề xuất điều chỉnh tồn kho (P0-1: Kiểm kê kho -> CEO duyệt)
    public enum StockAdjustmentStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }

    // Đề xuất điều chỉnh tồn kho do Warehouse Staff tạo sau khi kiểm kê thực tế, chờ CEO duyệt.
    // Chỉ khi Status = Approved thì Inventory.OnHandQuantity mới thực sự được cập nhật (xem StockAdjustmentService.DecideAsync).
    public class StockAdjustment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InventoryId { get; set; }

        // Snapshot OnHandQuantity của Inventory tại thời điểm tạo đề xuất (không tính lại theo tồn kho hiện tại)
        public int SystemQuantity { get; set; }

        // Số lượng đếm thực tế do Warehouse Staff ghi nhận
        public int PhysicalQuantity { get; set; }

        // PhysicalQuantity - SystemQuantity, lưu cố định tại thời điểm tạo để hiển thị; khi Approve,
        // giá trị này được CỘNG (delta) vào OnHandQuantity hiện tại lúc duyệt, không ghi đè tuyệt đối.
        public int Variance { get; set; }

        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;

        public StockAdjustmentStatus Status { get; set; } = StockAdjustmentStatus.Pending;

        public Guid ProposedByUserId { get; set; }
        public DateTime ProposedAt { get; set; } = DateTime.UtcNow;

        public Guid? DecidedByUserId { get; set; }
        public DateTime? DecidedAt { get; set; }

        [MaxLength(1000)]
        public string? DecisionNote { get; set; }

        // Navigation Properties
        public Inventory Inventory { get; set; } = null!;
        public User ProposedByUser { get; set; } = null!;
        public User? DecidedByUser { get; set; }
    }
}
