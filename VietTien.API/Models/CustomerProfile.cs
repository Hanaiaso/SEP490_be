namespace VietTien.API.Models
{
    public class CustomerProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string? TaxCode { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyAddress { get; set; }
        public string? InvoiceEmail { get; set; }
        public string? Representative { get; set; }
        public string? CompanyPhone { get; set; }

        // Lưu cấu hình giá B2B thỏa thuận dưới dạng chuỗi JSON hoặc Snapshot
        public string? SavedB2BPriceSnapshot { get; set; }

        public Guid? AssignedSalesStaffId { get; set; }

        // Nguồn phân bổ hiện tại (giá trị thuộc AssignmentSource) và ghi chú của Sale phụ trách
        public string? AssignmentSource { get; set; }
        public DateTime? AssignedAt { get; set; }
        public string? SalesNote { get; set; }

        public decimal AvailableCredit { get; set; } = 0m;

        // Navigation Properties
        public User User { get; set; } = null!;
        public User? AssignedSalesStaff { get; set; }
        public ICollection<Address> Addresses { get; set; } = new List<Address>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<CustomerDebt> Debts { get; set; } = new List<CustomerDebt>();
    }
}
