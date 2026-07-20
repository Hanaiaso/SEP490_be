namespace VietTien.API.Models
{
    public class PayrollDetail
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid MonthlyPayrollId { get; set; }
        public Guid EmployeeId { get; set; }
        public int UnpaidLeaves { get; set; } // Số ngày nghỉ thực tế
        public decimal Deductions { get; set; } // Phạt tự động nếu nghỉ > 4 ngày
        public decimal FinalNetSalary { get; set; }

        public MonthlyPayroll MonthlyPayroll { get; set; } = null!;
        public User Employee { get; set; } = null!; // Kết nối trực tiếp tới bảng Users
    }
}
