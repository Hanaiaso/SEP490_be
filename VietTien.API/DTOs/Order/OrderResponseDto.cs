using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class OrderResponseDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal FinalPayment { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? SePayQrUrl { get; set; }
        public string? InvoicePdfUrl { get; set; }
    }
}
