using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ISalesStaffDashboardService
    {
        // callerId luôn tự lấy từ JWT ở controller — Sales Staff không thể truyền id người khác.
        Task<SalesStaffDashboardDto> GetDashboardAsync(Guid callerId, DateTime from, DateTime to);
    }
}
