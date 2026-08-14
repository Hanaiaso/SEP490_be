using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SalesTargetService : ISalesTargetService
    {
        private readonly ApplicationDbContext _context;

        public SalesTargetService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SalesStaffTargetDto> SetTargetAsync(SetSalesTargetRequest request, Guid setByUserId)
        {
            var staff = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.SalesStaffId)
                ?? throw new KeyNotFoundException("Không tìm thấy nhân viên.");
            if (staff.Role != SystemRole.SalesStaff)
                throw new InvalidOperationException("Chỉ có thể đặt mục tiêu doanh thu cho tài khoản Sales Staff.");

            var target = await _context.SalesRevenueTargets.FirstOrDefaultAsync(t =>
                t.SalesStaffId == request.SalesStaffId && t.Year == request.Year && t.Month == request.Month);

            if (target == null)
            {
                target = new SalesRevenueTarget
                {
                    SalesStaffId = request.SalesStaffId,
                    Year = request.Year,
                    Month = request.Month,
                    TargetAmount = request.TargetAmount,
                    SetByUserId = setByUserId,
                };
                _context.SalesRevenueTargets.Add(target);
            }
            else
            {
                target.TargetAmount = request.TargetAmount;
                target.SetByUserId = setByUserId;
                target.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var revenue = await GetRevenueForMonthAsync(request.SalesStaffId, request.Year, request.Month);

            return new SalesStaffTargetDto
            {
                SalesStaffId = staff.Id,
                SalesStaffName = staff.FullName,
                Year = request.Year,
                Month = request.Month,
                TargetAmount = target.TargetAmount,
                ActualRevenue = revenue,
                AchievementRate = target.TargetAmount > 0 ? (double)(revenue / target.TargetAmount) : null,
                SetAt = target.UpdatedAt,
                SetByName = staff.Id == setByUserId ? staff.FullName : (await _context.Users.FindAsync(setByUserId))?.FullName,
            };
        }

        public async Task<List<SalesStaffTargetDto>> GetTeamTargetsAsync(int year, int month)
        {
            var staffList = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == SystemRole.SalesStaff && u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var targets = await _context.SalesRevenueTargets
                .AsNoTracking()
                .Include(t => t.SetByUser)
                .Where(t => t.Year == year && t.Month == month)
                .ToDictionaryAsync(t => t.SalesStaffId);

            var (from, to) = MonthRange(year, month);
            var revenueByStaff = await _context.Orders
                .Where(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt >= from && o.CreatedAt < to
                            && o.SalesStaffId != null)
                .GroupBy(o => o.SalesStaffId!.Value)
                .Select(g => new { SalesStaffId = g.Key, Revenue = g.Sum(o => o.AmountPaid) })
                .ToDictionaryAsync(x => x.SalesStaffId, x => x.Revenue);

            var result = new List<SalesStaffTargetDto>();
            foreach (var staff in staffList)
            {
                targets.TryGetValue(staff.Id, out var target);
                revenueByStaff.TryGetValue(staff.Id, out var revenue);
                var targetAmount = target?.TargetAmount ?? 0m;

                result.Add(new SalesStaffTargetDto
                {
                    SalesStaffId = staff.Id,
                    SalesStaffName = staff.FullName,
                    Year = year,
                    Month = month,
                    TargetAmount = targetAmount,
                    ActualRevenue = revenue,
                    AchievementRate = targetAmount > 0 ? (double)(revenue / targetAmount) : null,
                    SetAt = target?.UpdatedAt,
                    SetByName = target?.SetByUser?.FullName,
                });
            }

            return result;
        }

        public async Task<(decimal Target, decimal Revenue)> GetCurrentMonthTargetAndRevenueAsync(Guid? salesStaffId)
        {
            var now = DateTime.UtcNow;
            var (from, to) = MonthRange(now.Year, now.Month);

            if (salesStaffId.HasValue)
            {
                var target = await _context.SalesRevenueTargets
                    .Where(t => t.SalesStaffId == salesStaffId.Value && t.Year == now.Year && t.Month == now.Month)
                    .Select(t => (decimal?)t.TargetAmount)
                    .FirstOrDefaultAsync() ?? 0m;
                var revenue = await GetRevenueForMonthAsync(salesStaffId.Value, now.Year, now.Month);
                return (target, revenue);
            }

            // salesStaffId = null -> toàn đội: cộng dồn mục tiêu và doanh thu của mọi Sales Staff.
            var teamTarget = await _context.SalesRevenueTargets
                .Where(t => t.Year == now.Year && t.Month == now.Month)
                .SumAsync(t => (decimal?)t.TargetAmount) ?? 0m;
            var teamRevenue = await _context.Orders
                .Where(o => o.OrderStatus == OrderStatus.Completed && o.CreatedAt >= from && o.CreatedAt < to)
                .SumAsync(o => (decimal?)o.AmountPaid) ?? 0m;
            return (teamTarget, teamRevenue);
        }

        private async Task<decimal> GetRevenueForMonthAsync(Guid salesStaffId, int year, int month)
        {
            var (from, to) = MonthRange(year, month);
            return await _context.Orders
                .Where(o => o.SalesStaffId == salesStaffId && o.OrderStatus == OrderStatus.Completed
                            && o.CreatedAt >= from && o.CreatedAt < to)
                .SumAsync(o => (decimal?)o.AmountPaid) ?? 0m;
        }

        private static (DateTime From, DateTime ToExclusive) MonthRange(int year, int month)
        {
            var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            return (from, from.AddMonths(1));
        }
    }
}
