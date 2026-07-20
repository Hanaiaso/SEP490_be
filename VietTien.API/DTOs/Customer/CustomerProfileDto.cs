namespace VietTien.API.DTOs.Customer
{
    public class CustomerProfileDto
    {
        public string? TaxCode { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyAddress { get; set; }
        public string? InvoiceEmail { get; set; }
        public string? Representative { get; set; }
        public string? CompanyPhone { get; set; }
        public decimal AvailableCredit { get; set; }
    }
}
