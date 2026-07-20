using System;

namespace VietTien.API.DTOs.Order
{
    public class DirectOrderResponseDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal FinalPayment { get; set; }
        public string? InvoicePdfUrl { get; set; }
    }
}
