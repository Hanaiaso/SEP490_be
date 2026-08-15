using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SalesManagerDashboardService : ISalesManagerDashboardService
    {
        private const int ListLimit = 20;

        private readonly ApplicationDbContext _context;
        private readonly IKpiService _kpiService;

        public SalesManagerDashboardService(ApplicationDbContext context, IKpiService kpiService)
        {
            _context = context;
            _kpiService = kpiService;
        }

        public async Task<SalesManagerDashboardDto> GetDashboardAsync(DateTime from, DateTime to)
        {
            // "Toàn đội" = toàn bộ đơn hàng trong hệ thống (mọi đơn đều có SalesStaffId snapshot khi tạo).
            var teamKpi = await _kpiService.GetSnapshotAsync(null, from, to);

            var salesStaff = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == SystemRole.SalesStaff && u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var staffBreakdown = new List<SalesStaffKpiDto>();
            foreach (var staff in salesStaff)
            {
                var kpi = await _kpiService.GetSnapshotAsync(staff.Id, from, to);
                staffBreakdown.Add(new SalesStaffKpiDto
                {
                    SalesStaffId = staff.Id,
                    SalesStaffName = staff.FullName,
                    Kpi = kpi
                });
            }

            var openExceptions = await _context.PaymentExceptions
                .AsNoTracking()
                .Include(pe => pe.Order)
                .Where(pe => pe.Status == "OPEN")
                .OrderBy(pe => pe.CreatedAt)
                .Take(ListLimit)
                .Select(pe => new PaymentExceptionDto
                {
                    Id = pe.Id,
                    OrderId = pe.OrderId,
                    OrderCode = pe.Order.OrderCode,
                    ReasonCode = pe.ReasonCode,
                    Description = pe.Description,
                    Status = pe.Status,
                    RetryCount = pe.RetryCount,
                    CreatedAt = pe.CreatedAt
                })
                .ToListAsync();

            // Field OverdueDays trong CustomerDebt chỉ được set 1 lần lúc tạo (luôn = 0) và không có
            // job nào cập nhật theo thời gian — tính lại số ngày quá hạn thực tế ở đây (in-memory,
            // giống OrderService.GetDebtsAsync) thay vì tin giá trị đã lưu hoặc dùng EF.Functions.DateDiffDay
            // (chỉ dịch được sang SQL Server, không chạy được trên EF InMemory dùng trong unit test).
            var allInDebtOrders = await _context.CustomerDebts
                .AsNoTracking()
                .Include(d => d.CustomerProfile)
                .Include(d => d.Order)
                .Where(d => d.Status == DebtStatus.InDebt)
                .ToListAsync();

            var overdueDebts = allInDebtOrders
                .OrderByDescending(d => (DateTime.UtcNow - d.CreatedAt).TotalDays)
                .Take(ListLimit)
                .Select(d => new CustomerDebtDto
                {
                    Id = d.Id,
                    CustomerProfileId = d.CustomerProfileId,
                    CustomerName = d.CustomerProfile.Representative ?? d.CustomerProfile.CompanyName ?? "Khách hàng",
                    OrderId = d.OrderId,
                    OrderCode = d.Order.OrderCode,
                    DebtAmount = d.DebtAmount,
                    OverdueDays = Math.Max(0, (int)(DateTime.UtcNow - d.CreatedAt).TotalDays)
                })
                .ToList();

            // "Hôm nay" tính theo giờ Việt Nam (UTC+7), không phải ngày UTC — tránh tính nhầm các vi
            // phạm SLA phát sinh vào sáng sớm giờ VN (vẫn là ngày UTC hôm trước) sang hôm qua.
            var todayStart = DateTime.UtcNow.AddHours(7).Date.AddHours(-7);
            var codSlaBreachCountToday = await _context.Notifications
                .AsNoTracking()
                .Where(n => n.Type == NotificationType.SYS_04_CodUnconfirmed30m && n.CreatedAt >= todayStart)
                .Select(n => n.ReferenceId)
                .Distinct()
                .CountAsync();

            var pendingQuotationApprovalCount = await _context.Quotations
                .CountAsync(q => q.Status == QuotationStatus.PendingManager);
            var pendingSalesChangeRequestCount = await _context.SalesChangeRequests
                .CountAsync(r => r.Status == SalesChangeRequestStatus.Pending);
            var pendingDeliveryConflictCount = await _context.DeliveryScheduleConflicts
                .CountAsync(c => c.Status == DeliveryConflictStatus.Pending);
            var pendingMarketingApprovalCount = await _context.MarketingPosts
                .CountAsync(p => p.Status == MarketingPostStatus.Submitted);

            return new SalesManagerDashboardDto
            {
                TeamKpi = teamKpi,
                StaffBreakdown = staffBreakdown,
                OpenExceptions = openExceptions,
                OverdueDebts = overdueDebts,
                CodSlaBreachCountToday = codSlaBreachCountToday,
                PendingQuotationApprovalCount = pendingQuotationApprovalCount,
                PendingSalesChangeRequestCount = pendingSalesChangeRequestCount,
                PendingDeliveryConflictCount = pendingDeliveryConflictCount,
                PendingMarketingApprovalCount = pendingMarketingApprovalCount
            };
        }
    }
}
