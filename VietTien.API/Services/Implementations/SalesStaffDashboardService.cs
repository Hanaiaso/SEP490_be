using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SalesStaffDashboardService : ISalesStaffDashboardService
    {
        private readonly IKpiService _kpiService;

        public SalesStaffDashboardService(IKpiService kpiService)
        {
            _kpiService = kpiService;
        }

        public async Task<SalesStaffDashboardDto> GetDashboardAsync(Guid callerId, DateTime from, DateTime to)
        {
            var kpi = await _kpiService.GetSnapshotAsync(callerId, from, to);
            return new SalesStaffDashboardDto { Kpi = kpi };
        }
    }
}
