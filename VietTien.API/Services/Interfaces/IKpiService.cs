using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface IKpiService
    {
        // salesStaffId = null -> tính KPI toàn hệ thống (dùng cho CEO/Sales Manager team-wide).
        Task<KpiSnapshotDto> GetSnapshotAsync(Guid? salesStaffId, DateTime from, DateTime to);
    }
}
