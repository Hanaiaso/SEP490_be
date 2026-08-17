using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AdminDashboardService : IAdminDashboardService
    {
        private const int TopListLimit = 4;
        private const int StaleOrderHoursThreshold = 48;

        private static readonly OrderStatus[] ReceivedStatuses = { OrderStatus.Confirmed, OrderStatus.Processing };
        private static readonly OrderStatus[] CancelledStatuses = { OrderStatus.Cancelled, OrderStatus.CancelledReallocated };
        private static readonly QuotationStatus[] PendingQuotationStatuses = { QuotationStatus.PendingManager, QuotationStatus.PendingCeo };

        private readonly ApplicationDbContext _context;
        private readonly IKpiService _kpiService;
        private readonly IInventoryService _inventoryService;

        public AdminDashboardService(ApplicationDbContext context, IKpiService kpiService, IInventoryService inventoryService)
        {
            _context = context;
            _kpiService = kpiService;
            _inventoryService = inventoryService;
        }

        public async Task<AdminDashboardDto> GetDashboardAsync(DateTime from, DateTime to)
        {
            var periodLength = to - from;
            var previousFrom = from - periodLength;
            var previousTo = from;

            var kpi = await _kpiService.GetSnapshotAsync(null, from, to);
            var previousKpi = await _kpiService.GetSnapshotAsync(null, previousFrom, previousTo);

            var receivedOrderCount = await _context.Orders.CountAsync(o => ReceivedStatuses.Contains(o.OrderStatus));
            var shippingOrderCount = await _context.Orders.CountAsync(o => o.DeliveryStatus == DeliveryStatus.InDelivery);

            var deliveredOrderCount = await _context.Orders.CountAsync(o =>
                o.DeliveryStatus == DeliveryStatus.Delivered && o.CreatedAt >= from && o.CreatedAt <= to);

            var cancelledOrderCount = await _context.Orders.CountAsync(o =>
                CancelledStatuses.Contains(o.OrderStatus) && o.CreatedAt >= from && o.CreatedAt <= to);
            var previousCancelledOrderCount = await _context.Orders.CountAsync(o =>
                CancelledStatuses.Contains(o.OrderStatus) && o.CreatedAt >= previousFrom && o.CreatedAt <= previousTo);

            var pendingQuotationApprovalCount = await _context.Quotations
                .CountAsync(q => PendingQuotationStatuses.Contains(q.Status));

            var staleThreshold = DateTime.UtcNow.AddHours(-StaleOrderHoursThreshold);
            var staleOrderCount = await _context.Orders.CountAsync(o =>
                ReceivedStatuses.Contains(o.OrderStatus) && o.CreatedAt <= staleThreshold);

            var (lowStockCount, lowStockItems) = await GetLowStockAsync();
            var topProducts = await GetTopProductsAsync(from, to);
            var topCustomers = await GetTopCustomersAsync(from, to);

            return new AdminDashboardDto
            {
                PeriodFrom = from,
                PeriodTo = to,
                Revenue = kpi.Revenue,
                PreviousPeriodRevenue = previousKpi.Revenue,
                ReceivedOrderCount = receivedOrderCount,
                ShippingOrderCount = shippingOrderCount,
                DeliveredOrderCount = deliveredOrderCount,
                CancelledOrderCount = cancelledOrderCount,
                PreviousPeriodCancelledOrderCount = previousCancelledOrderCount,
                PendingQuotationApprovalCount = pendingQuotationApprovalCount,
                StaleOrderCount = staleOrderCount,
                LowStockAlertCount = lowStockCount,
                LowStockItems = lowStockItems,
                TopProducts = topProducts,
                TopCustomers = topCustomers,
            };
        }

        // Dùng lại InventoryService (nguồn xác thực duy nhất, gộp cả Product + Material qua mọi kho)
        // thay vì tự tính lại — trước đây đọc thẳng Inventory.ReorderThreshold (field per-row đã chết,
        // không có nơi nào set) nên LowStockAlertCount trên Dashboard Admin luôn = 0.
        private async Task<(int Count, List<AdminLowStockItemDto> Items)> GetLowStockAsync()
        {
            var alerts = await _inventoryService.GetLowStockAlertsAsync();

            var ordered = alerts
                .OrderBy(a => a.Threshold == 0 ? 0 : a.AvailableQuantity / a.Threshold)
                .ToList();

            var items = ordered
                .Take(TopListLimit)
                .Select(a => new AdminLowStockItemDto
                {
                    Name = a.ItemName,
                    AvailableQuantity = (int)a.AvailableQuantity,
                    Unit = a.Unit ?? string.Empty,
                    Urgent = a.Threshold > 0 && a.AvailableQuantity / a.Threshold < 0.5,
                })
                .ToList();

            return (ordered.Count, items);
        }

        private async Task<List<AdminTopProductDto>> GetTopProductsAsync(DateTime from, DateTime to)
        {
            var raw = await _context.OrderItems
                .AsNoTracking()
                .Where(oi => oi.Order.CreatedAt >= from && oi.Order.CreatedAt <= to
                    && !CancelledStatuses.Contains(oi.Order.OrderStatus))
                .GroupBy(oi => oi.ProductId)
                .Select(g => new { ProductId = g.Key, OrderCount = g.Select(x => x.OrderId).Distinct().Count() })
                .OrderByDescending(x => x.OrderCount)
                .Take(TopListLimit)
                .ToListAsync();

            var productIds = raw.Select(x => x.ProductId).ToList();
            var names = await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            return raw
                .Select(x => new AdminTopProductDto
                {
                    Name = names.TryGetValue(x.ProductId, out var n) ? n : "N/A",
                    OrderCount = x.OrderCount,
                })
                .ToList();
        }

        private async Task<List<AdminTopCustomerDto>> GetTopCustomersAsync(DateTime from, DateTime to)
        {
            var raw = await _context.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt >= from && o.CreatedAt <= to)
                .GroupBy(o => o.CustomerProfileId)
                .Select(g => new { CustomerProfileId = g.Key, TotalSpent = g.Sum(x => x.FinalPayment) })
                .OrderByDescending(x => x.TotalSpent)
                .Take(TopListLimit)
                .ToListAsync();

            var customerIds = raw.Select(x => x.CustomerProfileId).ToList();
            var names = await _context.CustomerProfiles
                .AsNoTracking()
                .Where(cp => customerIds.Contains(cp.Id))
                .ToDictionaryAsync(cp => cp.Id, cp => cp.Representative ?? cp.CompanyName ?? "Khách hàng");

            return raw
                .Select(x => new AdminTopCustomerDto
                {
                    Name = names.TryGetValue(x.CustomerProfileId, out var n) ? n : "Khách hàng",
                    TotalSpent = x.TotalSpent,
                })
                .ToList();
        }
    }
}
