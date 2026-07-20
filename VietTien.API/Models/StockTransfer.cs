namespace VietTien.API.Models
{
    public enum StockTransferStatus { Draft, Dispatched, Received, Cancelled }

    public class StockTransfer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty; // TR-2026-001

        public Guid SourceWarehouseId { get; set; }
        public Guid DestinationWarehouseId { get; set; }
        public Guid CreatedByUserId { get; set; }

        public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }

        public string? Note { get; set; }
        public string? ReceiveNote { get; set; }
        public string? ProofImageUrl { get; set; }
        public string? NotificationEmail { get; set; }

        // Navigation
        public Warehouse SourceWarehouse { get; set; } = null!;
        public Warehouse DestinationWarehouse { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
    }
}
