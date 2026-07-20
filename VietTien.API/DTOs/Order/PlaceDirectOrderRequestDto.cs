using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class PlaceDirectOrderRequestDto
    {
        [Required]
        public string CustomerName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public string? Address { get; set; }

        [Required]
        public decimal TotalAmount { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal VatAmount { get; set; }

        [Required]
        public decimal FinalPayment { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        public string? InvoicePdfBase64 { get; set; } // Uploaded base64 from client-side PDF

        public string? OrderCode { get; set; }

        [Required]
        public List<DirectOrderItemDto> Items { get; set; } = new();
    }

    public class DirectOrderItemDto
    {
        [Required]
        public Guid ProductId { get; set; }

        [Required]
        public int Quantity { get; set; }

        [Required]
        public decimal Price { get; set; }
    }
}
