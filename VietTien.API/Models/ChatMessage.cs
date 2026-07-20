namespace VietTien.API.Models
{
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuotationId { get; set; }
        public Guid SenderId { get; set; }
        
        public string MessageText { get; set; } = string.Empty;
        public string? FileUrl { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;

        // Navigation Properties
        public Quotation Quotation { get; set; } = null!;
        public User Sender { get; set; } = null!;
    }
}
