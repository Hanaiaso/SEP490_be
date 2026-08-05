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

            var overdueDebts = await _context.CustomerDebts
                .AsNoTracking()
                .Include(d => d.CustomerProfile)
                .Include(d => d.Order)
                .Where(d => d.Status == DebtStatus.InDebt)
                .OrderByDescending(d => d.OverdueDays)
                .Take(ListLimit)
                .Select(d => new CustomerDebtDto
                {
                    Id = d.Id,
                    CustomerProfileId = d.CustomerProfileId,
                    CustomerName = d.CustomerProfile.Representative ?? d.CustomerProfile.CompanyName ?? "Khách hàng",
                    OrderId = d.OrderId,
                    OrderCode = d.Order.OrderCode,
                    DebtAmount = d.DebtAmount,
                    OverdueDays = d.OverdueDays
                })
                .ToListAsync();

            var todayStart = DateTime.UtcNow.Date;
            var codSlaBreachCountToday = await _context.Notifications
                .AsNoTracking()
                .Where(n => n.Type == NotificationType.SYS_04_CodUnconfirmed30m && n.CreatedAt >= todayStart)
                .Select(n => n.ReferenceId)
                .Distinct()
                .CountAsync();

            return new SalesManagerDashboardDto
            {
                TeamKpi = teamKpi,
                StaffBreakdown = staffBreakdown,
                OpenExceptions = openExceptions,
                OverdueDebts = overdueDebts,
                CodSlaBreachCountToday = codSlaBreachCountToday
            };
        }
    }
}
