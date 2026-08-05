using System.ComponentModel.DataAnnotations;
using VietTien.API.Infrastructure.Validation;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class PlaceOrderRequestDto
    {
        public Guid? AddressId { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
        public string? Notes { get; set; }

        public bool RequiresRedInvoice { get; set; } = false;

        [Base64Pdf(10)]
        public string? InvoicePdfBase64 { get; set; }

        public string? OrderCode { get; set; }
    }
}
