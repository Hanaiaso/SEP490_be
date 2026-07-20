namespace VietTien.API.Models
{
    public enum PurchaseOrderStatus { Draft, Issued, SentToWarehouse, PartiallyReceived, FullyReceived, DiscrepancyReview, Closed, Cancelled }

    public class PurchaseOrder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty; // e.g., PO-2026-001
        
        public Guid CreatedById { get; set; } // CEO
        public Guid SupplierId { get; set; }
        public Guid WarehouseId { get; set; } // Kho nhận hàng

        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpectedDeliveryDate { get; set; }
        public DateTime? IssuedAt { get; set; } // Thời điểm phát hành

        public string? Note { get; set; }
        public string? DeliveryTerms { get; set; } // Điều kiện giao nhận

        // Navigation Properties
        public User CreatedBy { get; set; } = null!;
        public Supplier Supplier { get; set; } = null!;
        public Warehouse Warehouse { get; set; } = null!;
        public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
        public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
    }
}
