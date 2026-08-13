namespace VietTien.API.Models
{
    // INV-01: kiểm kê kho 2 bước (snapshot lý thuyết -> ghi số đếm thực tế), khác với
    // SubmitShiftInventoryCountAsync (ghi đè OnHandQuantity ngay trong 1 bước). Ở bước ghi số đếm,
    // InventoryBalance (Inventory.OnHandQuantity) KHÔNG bị thay đổi — đối chiếu chênh lệch để cộng
    // vào tồn kho thật là bước tiếp theo, ngoài phạm vi đóng case này (có thể tái dùng luồng duyệt
    // CEO của StockAdjustment sau này thay vì tạo thêm 1 cơ chế duyệt thứ 2).
    public enum CountSessionStatus { Draft, TheoreticalLocked, Completed }

    public class InventoryCountSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid WarehouseId { get; set; }
        public CountSessionStatus Status { get; set; } = CountSessionStatus.Draft;

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? TheoreticalLockedAt { get; set; }

        // Navigation Properties
        public Warehouse Warehouse { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public ICollection<InventoryCountLine> Lines { get; set; } = new List<InventoryCountLine>();
    }

    public class InventoryCountLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid InventoryCountSessionId { get; set; }
        public Guid InventoryId { get; set; }

        public int TheoreticalQuantity { get; set; }
        public int? ActualQuantity { get; set; }
        public DateTime? CountedAt { get; set; }

        // Navigation Properties
        public InventoryCountSession InventoryCountSession { get; set; } = null!;
        public Inventory Inventory { get; set; } = null!;
    }
}
