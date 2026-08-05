using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public class Quotation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }
        public Guid? SalesStaffId { get; set; }
        public Guid? CartId { get; set; }
        public Guid? AcceptedVersionId { get; set; }

        public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

        // Concurrency token: chống 2 Sale cùng "chốt" 1 báo giá (PickUpQuotationAsync) ghi đè
        // SalesStaffId của nhau -> throw DbUpdateConcurrencyException, middleware map sẵn thành 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public decimal OriginalTotal { get; set; }

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;
        public DateTime? ValidUntil { get; set; }

        public string? GeneralNote { get; set; }

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public User? SalesStaff { get; set; }
        
        public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
        public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
        public ICollection<QuotationVersion> Versions { get; set; } = new List<QuotationVersion>();
    }

    public enum QuotationStatus
    {
        Draft,
        Negotiating,
        PendingManager,
        PendingCeo,
        Approved,
        CustomerAccepted,
        CustomerRejected,
        Expired,
        Cancelled
    }
}
