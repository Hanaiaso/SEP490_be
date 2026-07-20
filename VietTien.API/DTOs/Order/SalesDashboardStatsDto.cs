using System;
using System.Collections.Generic;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Order
{
    public class SalesDashboardStatsDto
    {
        public DashboardKpiDto Kpi { get; set; } = new();
        public List<DashboardOrderDto> RecentOrders { get; set; } = new();
        public List<DashboardUrgentOrderDto> UrgentOrders { get; set; } = new();
        public List<DashboardWarehouseQueueDto> WarehouseQueue { get; set; } = new();
        public List<DashboardQuoteRequestDto> QuoteRequests { get; set; } = new();
        public List<DashboardTopProductDto> TopProducts { get; set; } = new();
        public List<DashboardRevenueDayDto> Last7DaysRevenue { get; set; } = new();
    }

    public class DashboardKpiDto
    {
        public int NewOrdersCount { get; set; }
        public int ProcessingOrdersCount { get; set; }
        public int ShippingOrdersCount { get; set; }
        public int DeliveredTodayCount { get; set; }
        public decimal RevenueToday { get; set; }
        public decimal PendingDebt { get; set; }
    }

    public class DashboardOrderDto
    {
        public Guid Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public decimal FinalPayment { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public string? InvoicePdfUrl { get; set; }
    }

    public class DashboardUrgentOrderDto
    {
        public string Id { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Deadline { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty; // critical, high, normal
    }

    public class DashboardWarehouseQueueDto
    {
        public string Id { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public int ItemsCount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class DashboardQuoteRequestDto
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public DateTime RequestDate { get; set; }
    }

    public class DashboardTopProductDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public class DashboardRevenueDayDto
    {
        public string Day { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal Target { get; set; }
    }
}
