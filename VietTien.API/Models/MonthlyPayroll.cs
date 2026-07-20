namespace VietTien.API.Models
{
    public class MonthlyPayroll
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TotalAllocatedSalary { get; set; } // Kết nối đưa sang Cost Model tính giá gốc
        public bool IsLocked { get; set; } = false;

        public ICollection<PayrollDetail> Details { get; set; } = new List<PayrollDetail>();
    }
}
