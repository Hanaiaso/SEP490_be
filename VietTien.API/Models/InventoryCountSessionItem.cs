using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public class InventoryCountSessionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SessionId { get; set; }
        public Guid InventoryId { get; set; }

        // Số lượng lý thuyết chốt tại thời điểm MỞ phiên (snapshot Inventory.OnHandQuantity) — khoá,
        // không đổi dù tồn kho biến động trong lúc phiên đang mở.
        public int SystemQuantity { get; set; }

        // Số đếm thực tế do Warehouse Staff nhập; null = chưa đếm dòng này.
        public int? PhysicalQuantity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        // Chênh lệch hiển thị — không map vào DB (get-only, giống Inventory.AvailableQuantity).
        public int? Variance => PhysicalQuantity.HasValue ? PhysicalQuantity.Value - SystemQuantity : null;

        // true nếu chênh lệch trong ngưỡng và đã được áp dụng thẳng vào Inventory lúc đóng phiên.
        public bool AutoApplied { get; set; }

        // Set nếu chênh lệch vượt ngưỡng -> đã tạo StockAdjustment (Pending) chờ CEO duyệt riêng.
        public Guid? StockAdjustmentId { get; set; }

        // Navigation Properties
        public InventoryCountSession Session { get; set; } = null!;
        public Inventory Inventory { get; set; } = null!;
        public StockAdjustment? StockAdjustment { get; set; }
    }
}
