namespace VietTien.API.Models
{
    public enum GoodsReceiptStatus { Draft, Posted, Cancelled }

    public class GoodsReceipt
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PurchaseOrderId { get; set; }
        public Guid ReceivedByUserId { get; set; }         // Nhân viên kho
        public string Code { get; set; } = string.Empty;   // GR-2026-001
        
        public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;
        public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }
        
        // Navigation
        public PurchaseOrder PurchaseOrder { get; set; } = null!;
        public User ReceivedByUser { get; set; } = null!;
        public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
    }
}
