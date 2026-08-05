using System.ComponentModel.DataAnnotations;

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

        // Concurrency token: chống 2 request đồng thời UploadProof/PostReceipt cùng 1 phiếu nhận hàng
        // ghi đè trạng thái/proof của nhau -> throw DbUpdateConcurrencyException, middleware map sẵn thành 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }
        public string? ImageProofUrl { get; set; }
        
        // Navigation
        public PurchaseOrder PurchaseOrder { get; set; } = null!;
        public User ReceivedByUser { get; set; } = null!;
        public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
    }
}
