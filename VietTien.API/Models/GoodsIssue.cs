using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum GoodsIssueStatus { Draft, ProofPending, ProofUploaded, Posted, Cancelled, Reversed }
    public enum GoodsIssueType { SalesOrder, StockTransfer, ProductionMaterial, Other }

    public class GoodsIssue
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty; // GI-2026-001
        
        public GoodsIssueType Type { get; set; }
        public Guid? ReferenceId { get; set; } // OrderId, StockTransferId, etc.
        public Guid WarehouseId { get; set; }
        public Guid IssuedByUserId { get; set; }

        public GoodsIssueStatus Status { get; set; } = GoodsIssueStatus.Draft;

        // Concurrency token: chống 2 request đồng thời Post/Reversal cùng 1 phiếu xuất kho ghi đè
        // trạng thái của nhau -> throw DbUpdateConcurrencyException, middleware map sẵn thành 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? IssueDate { get; set; }
        
        [MaxLength(1000)]
        public string? ImageProofUrl { get; set; }

        // Mở rộng nghiệp vụ Xuất nguyên liệu cho sản xuất (WF-17 / Luồng 11)
        [MaxLength(200)]
        public string? ExternalRecipientName { get; set; } // Người nhận ngoài hệ thống

        [MaxLength(200)]
        public string? Department { get; set; } // Bộ phận sản xuất nhận

        public DateTime? ReceivedAt { get; set; } // Thời điểm thực tế nhận hàng

        [MaxLength(100)]
        public string? PaperDocumentNumber { get; set; } // Số biên bản giấy (Duy nhất)

        [MaxLength(500)]
        public string? UsagePurpose { get; set; } // Mục đích sử dụng

        public Guid? ReversalForIssueId { get; set; } // Trỏ tới phiếu gốc nếu đây là phiếu đảo
        
        [MaxLength(500)]
        public string? ReversalReason { get; set; }

        public bool IsReversal { get; set; } = false;

        public string? Note { get; set; }

        // Navigation
        public Warehouse Warehouse { get; set; } = null!;
        public User IssuedByUser { get; set; } = null!;
        public ICollection<GoodsIssueItem> Items { get; set; } = new List<GoodsIssueItem>();
    }
}
