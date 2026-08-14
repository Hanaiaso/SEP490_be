namespace VietTien.API.Models
{
    /// <summary>Mục tiêu doanh thu hàng tháng do Sales Manager đặt cho từng Sales Staff.</summary>
    public class SalesRevenueTarget
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SalesStaffId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; } // 1-12

        public decimal TargetAmount { get; set; }

        public Guid SetByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public User SalesStaff { get; set; } = null!;
        public User SetByUser { get; set; } = null!;
    }
}
