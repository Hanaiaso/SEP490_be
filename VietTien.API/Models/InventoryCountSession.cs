using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    // UC-47/UC-54: Phiên kiểm kê tồn kho theo Warehouse (DEF-L4-003 phần "count-sessions").
    public enum InventoryCountSessionStatus
    {
        Open = 0,
        Closed = 1
    }

    // Khi mở phiên, hệ thống chốt (snapshot) OnHandQuantity của MỌI dòng tồn kho thuộc kho đó vào
    // InventoryCountSessionItem.SystemQuantity — số này KHÔNG đổi dù tồn kho thực tế biến động
    // trong lúc phiên đang mở. Kho nhập số đếm thực tế cho từng dòng, rồi đóng phiên ("post"):
    // dòng chênh lệch trong ngưỡng cấu hình (SystemConfig INVENTORY_COUNT_VARIANCE_THRESHOLD) được
    // áp dụng thẳng vào tồn kho; dòng vượt ngưỡng tự động tạo StockAdjustment (Pending) để CEO
    // duyệt riêng — tồn kho của dòng đó KHÔNG đổi cho tới khi CEO duyệt (xem InventoryCountSessionService).
    public class InventoryCountSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid WarehouseId { get; set; }

        public InventoryCountSessionStatus Status { get; set; } = InventoryCountSessionStatus.Open;

        [MaxLength(500)]
        public string? Note { get; set; }

        public Guid OpenedByUserId { get; set; }
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

        public Guid? ClosedByUserId { get; set; }
        public DateTime? ClosedAt { get; set; }

        // Navigation Properties
        public Warehouse Warehouse { get; set; } = null!;
        public User OpenedByUser { get; set; } = null!;
        public User? ClosedByUser { get; set; }
        public ICollection<InventoryCountSessionItem> Items { get; set; } = new List<InventoryCountSessionItem>();
    }
}
