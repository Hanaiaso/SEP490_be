using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface IAdminDashboardService
    {
        Task<AdminDashboardDto> GetDashboardAsync(DateTime from, DateTime to);
    }
}
