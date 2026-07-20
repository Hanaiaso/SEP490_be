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

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public Order Order { get; set; } = null!;
    }
}
