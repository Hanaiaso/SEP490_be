namespace VietTien.API.Models
{
    public class EmployeeSalary
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public decimal BaseSalary { get; set; }
        public DateTime EffectiveDate { get; set; }
        public bool IsApprovedByAdmin { get; set; } = false;

        // Navigation Property
        public User User { get; set; } = null!;
    }
}
