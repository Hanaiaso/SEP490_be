using System;
using System.Collections.Generic;

namespace VietTien.API.DTOs.Order
{
    public class SalesOrderQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? SearchQuery { get; set; }
        public string? Status { get; set; }
        public string? PaymentMethod { get; set; }
    }

    public class SalesOrderListDto
    {
        public Guid Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public decimal FinalPayment { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string FulfillmentStatus { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public int TotalQuantity { get; set; }
        public DateTime? PickingStartedAt { get; set; }
        public DateTime? PickingCompletedAt { get; set; }
        public string? InvoicePdfUrl { get; set; }
    }

    public class SalesOrderDetailDto : SalesOrderListDto
    {
        public string CustomerPhone { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount { get; set; }
        public string DeliveryStatus { get; set; } = string.Empty;
        public int? DeliveryVehicleId { get; set; }
        public string? DeliveryShift { get; set; }
        public DateTime? ScheduledDeliveryDate { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? CustomerSignatureUrl { get; set; }
        public string? DeliveryPhotoUrl { get; set; }
        public int FailedDeliveryCount { get; set; }
        public decimal AmountPaid { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CompanyName { get; set; }
        public List<SalesOrderItemDto> Items { get; set; } = new List<SalesOrderItemDto>();
    }

    public class SalesOrderItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductSku { get; set; } = string.Empty;
        public string? ProductImageUrl { get; set; }
        public int Quantity { get; set; }
        public decimal PriceSnapshot { get; set; }
        public decimal LineTotal { get; set; }
    }
}
