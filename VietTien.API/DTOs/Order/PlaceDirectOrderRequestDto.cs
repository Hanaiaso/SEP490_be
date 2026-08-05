using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using VietTien.API.Infrastructure.Validation;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class PlaceDirectOrderRequestDto
    {
        [Required]
        [MaxLength(200, ErrorMessage = "Tên khách hàng không được vượt quá 200 ký tự.")]
        public string CustomerName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? PhoneNumber { get; set; }

        [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
        public string? Address { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Tổng tiền phải lớn hơn hoặc bằng 0")]
        public decimal TotalAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Số tiền giảm giá phải lớn hơn hoặc bằng 0")]
        public decimal DiscountAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Tiền VAT phải lớn hơn hoặc bằng 0")]
        public decimal VatAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Số tiền thanh toán cuối cùng phải lớn hơn hoặc bằng 0")]
        public decimal FinalPayment { get; set; }

        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        [Base64Pdf(10)]
        public string? InvoicePdfBase64 { get; set; } // Uploaded base64 from client-side PDF

        public string? OrderCode { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất 1 sản phẩm.")]
        public List<DirectOrderItemDto> Items { get; set; } = new();
    }

    public class DirectOrderItemDto
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Đơn giá phải lớn hơn 0")]
        public decimal Price { get; set; }
    }
}
