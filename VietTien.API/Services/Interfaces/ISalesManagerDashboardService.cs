using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ISalesManagerDashboardService
    {
        Task<SalesManagerDashboardDto> GetDashboardAsync(DateTime from, DateTime to);
    }
}
