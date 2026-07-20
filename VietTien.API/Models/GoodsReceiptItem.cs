namespace VietTien.API.Models
{
    public class GoodsReceiptItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoodsReceiptId { get; set; }
        public Guid PurchaseOrderItemId { get; set; }
        
        public int AcceptedQuantity { get; set; }    // Đạt
        public int DamagedQuantity { get; set; }     // Hỏng
        public int ExcessQuantity { get; set; }      // Thừa
        public int ShortQuantity { get; set; }       // Thiếu
        public int WrongItemQuantity { get; set; }   // Sai chủng loại
        
        public string? BatchNumber { get; set; }     // Số lô
        public DateTime? ExpiryDate { get; set; }    // Hạn sử dụng
        public string? Note { get; set; }
        
        // Navigation
        public GoodsReceipt GoodsReceipt { get; set; } = null!;
        public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
    }
}
