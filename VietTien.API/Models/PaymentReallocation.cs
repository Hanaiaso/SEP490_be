namespace VietTien.API.Models
{
    public class PaymentReallocation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OriginalOrderId { get; set; }
        public Guid? ReplacementOrderId { get; set; }

        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Status could be ReallocatedToOrder or RefundedToCredit
        public string Status { get; set; } = "ReallocatedToOrder";

        // Navigation Properties
        public Order OriginalOrder { get; set; } = null!;
        public Order? ReplacementOrder { get; set; }
    }
}
