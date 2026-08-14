using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // Cỗ máy tính KPI dùng chung cho cả 3 dashboard (business.md §5).
    // Toàn bộ phép tính đẩy xuống SQL (Sum/Count/Average) — KHÔNG load nguyên bảng Order vào memory
    // (khác với cách làm cũ trong OrderService.GetSalesDashboardStatsAsync).
    public class KpiService : IKpiService
    {
        private static readonly DeliveryStatus[] DeliveryAttemptedStatuses =
        {
            DeliveryStatus.Delivered, DeliveryStatus.Failed, DeliveryStatus.PartiallyDelivered
        };

        private readonly ApplicationDbContext _context;

        public KpiService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<KpiSnapshotDto> GetSnapshotAsync(Guid? salesStaffId, DateTime from, DateTime to)
        {
            var completedOrders = _context.Orders
                .Where(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt >= from && o.CreatedAt <= to);
            if (salesStaffId.HasValue)
                completedOrders = completedOrders.Where(o => o.SalesStaffId == salesStaffId.Value);

            // Dùng AmountPaid (tiền THỰC thu) thay vì FinalPayment (giá trị đơn) — một đơn Completed
            // vẫn có thể PartiallyPaid nếu khách trả thiếu qua COD (phần còn lại nằm ở CustomerDebts),
            // dùng FinalPayment sẽ cộng nhầm cả phần khách chưa trả vào doanh thu.
            var revenue = await completedOrders.SumAsync(o => (decimal?)o.AmountPaid) ?? 0m;
            var completedCount = await completedOrders.CountAsync();

            // --- DeliverySuccessRate ---
            var deliveryAttempted = _context.Orders
                .Where(o => DeliveryAttemptedStatuses.Contains(o.DeliveryStatus) && o.CreatedAt >= from && o.CreatedAt <= to);
            if (salesStaffId.HasValue)
                deliveryAttempted = deliveryAttempted.Where(o => o.SalesStaffId == salesStaffId.Value);

            var deliveryAttemptedCount = await deliveryAttempted.CountAsync();
            var deliveredCount = await deliveryAttempted.CountAsync(o => o.DeliveryStatus == DeliveryStatus.Delivered);
            var deliverySuccessRate = deliveryAttemptedCount == 0 ? 0 : (double)deliveredCount / deliveryAttemptedCount;

            // --- ProcessingSpeedAvgHours ---
            var confirmedOrders = _context.Orders
                .Where(o => o.ConfirmedAt != null && o.CreatedAt >= from && o.CreatedAt <= to);
            if (salesStaffId.HasValue)
                confirmedOrders = confirmedOrders.Where(o => o.SalesStaffId == salesStaffId.Value);

            var hasConfirmed = await confirmedOrders.AnyAsync();
            double? processingSpeedAvgHours = null;
            if (hasConfirmed)
            {
                var avgMinutes = await confirmedOrders.AverageAsync(o => EF.Functions.DateDiffMinute(o.CreatedAt, o.ConfirmedAt!.Value));
                processingSpeedAvgHours = avgMinutes / 60.0;
            }

            // --- ReturningCustomerRate ---
            var customersInScope = await completedOrders.Select(o => o.CustomerProfileId).Distinct().ToListAsync();
            var returningCount = 0;
            if (customersInScope.Count > 0)
            {
                returningCount = await _context.Orders
                    .Where(o => o.OrderStatus == OrderStatus.Completed && customersInScope.Contains(o.CustomerProfileId))
                    .GroupBy(o => o.CustomerProfileId)
                    .CountAsync(g => g.Count() >= 2);
            }
            var returningCustomerRate = customersInScope.Count == 0 ? 0 : (double)returningCount / customersInScope.Count;

            return new KpiSnapshotDto
            {
                SalesStaffId = salesStaffId,
                PeriodFrom = from,
                PeriodTo = to,
                Revenue = revenue,
                CompletedOrderCount = completedCount,
                DeliverySuccessRate = deliverySuccessRate,
                DeliveryAttemptedOrderCount = deliveryAttemptedCount,
                ProcessingSpeedAvgHours = processingSpeedAvgHours,
                ReturningCustomerRate = returningCustomerRate,
                CustomersInScopeCount = customersInScope.Count
            };
        }
    }
}
