namespace VietTien.API.Models
{
    public class QuotationVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuotationId { get; set; }
        public int VersionNumber { get; set; }

        public decimal ProposedTotal { get; set; }
        public string? SalesNote { get; set; }       // Ghi chú từ Sales
        public string? ManagerNote { get; set; }      // Nhận xét từ Manager
        public string? CeoNote { get; set; }          // Lý do từ CEO

        public QuotationVersionStatus Status { get; set; } = QuotationVersionStatus.Draft;

        public Guid CreatedByUserId { get; set; }     // Sales tạo
        public Guid? ManagerApprovedByUserId { get; set; }
        public Guid? CeoApprovedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ManagerReviewedAt { get; set; }
        public DateTime? CeoReviewedAt { get; set; }
        public DateTime? ValidUntil { get; set; }

        // Navigation
        public Quotation Quotation { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public ICollection<QuotationVersionItem> Items { get; set; } = new List<QuotationVersionItem>();
    }

    public enum QuotationVersionStatus
    {
        Draft,
        PendingManager,
        ManagerApproved,
        ManagerRejected,
        PendingCeo,
        CeoApproved,
        CeoRejected,
        CustomerAccepted,
        CustomerRejected,
        Expired
    }
}
