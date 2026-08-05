using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ICeoDashboardService
    {
        Task<CeoDashboardDto> GetDashboardAsync(DateTime from, DateTime to);
    }
}
