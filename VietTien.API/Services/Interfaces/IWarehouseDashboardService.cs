using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IWarehouseDashboardService
    {
        Task<WarehouseDashboardDto> GetDashboardAsync();
    }
}
