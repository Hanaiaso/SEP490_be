using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Customer
{
    public class CustomerProfileDto
    {
        [MaxLength(50, ErrorMessage = "Mã số thuế không được vượt quá 50 ký tự.")]
        public string? TaxCode { get; set; }

        [MaxLength(300, ErrorMessage = "Tên công ty không được vượt quá 300 ký tự.")]
        public string? CompanyName { get; set; }

        [MaxLength(500, ErrorMessage = "Địa chỉ công ty không được vượt quá 500 ký tự.")]
        public string? CompanyAddress { get; set; }

        [EmailAddress(ErrorMessage = "Email xuất hóa đơn không hợp lệ.")]
        [MaxLength(256)]
        public string? InvoiceEmail { get; set; }

        [MaxLength(200, ErrorMessage = "Tên người đại diện không được vượt quá 200 ký tự.")]
        public string? Representative { get; set; }

        [Phone(ErrorMessage = "Số điện thoại công ty không hợp lệ.")]
        public string? CompanyPhone { get; set; }

        public decimal AvailableCredit { get; set; }
    }
}
