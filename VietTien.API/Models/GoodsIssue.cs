using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum GoodsIssueStatus { Draft, ProofPending, ProofUploaded, Posted, Cancelled }
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
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? IssueDate { get; set; }
        
        [MaxLength(1000)]
        public string? ImageProofUrl { get; set; }
        public string? Note { get; set; }

        // Navigation
        public Warehouse Warehouse { get; set; } = null!;
        public User IssuedByUser { get; set; } = null!;
        public ICollection<GoodsIssueItem> Items { get; set; } = new List<GoodsIssueItem>();
    }
}
