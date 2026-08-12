namespace VietTien.API.Models
{
    public enum DebtStatus { NotInDebt, InDebt, Settled }
    public class CustomerDebt
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }
        public Guid OrderId { get; set; }
        public decimal DebtAmount { get; set; }
        public DebtStatus Status { get; set; }
        public int OverdueDays { get; set; }

        // ─── P2-6: audit tạo/tất toán (UC-35) ───
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SettledAt { get; set; }
        public Guid? SettledByUserId { get; set; }
        public string? SettlementNote { get; set; }

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public Order Order { get; set; } = null!;
        public User? SettledByUser { get; set; }
    }
}
