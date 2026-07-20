using System.ComponentModel.DataAnnotations;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class PlaceOrderRequestDto
    {
        public Guid? AddressId { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        public string? Notes { get; set; }

        public bool RequiresRedInvoice { get; set; } = false;

        public string? InvoicePdfBase64 { get; set; }

        public string? OrderCode { get; set; }
    }
}
