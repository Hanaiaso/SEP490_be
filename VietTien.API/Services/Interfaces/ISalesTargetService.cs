using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ISalesTargetService
    {
        /// <summary>Sales Manager đặt/cập nhật mục tiêu doanh thu tháng cho 1 Sales Staff.</summary>
        Task<SalesStaffTargetDto> SetTargetAsync(SetSalesTargetRequest request, Guid setByUserId);

        /// <summary>Danh sách toàn bộ Sales Staff đang hoạt động kèm mục tiêu + doanh thu thực tế của tháng chỉ định.</summary>
        Task<List<SalesStaffTargetDto>> GetTeamTargetsAsync(int year, int month);

        /// <summary>Mục tiêu + doanh thu thực tế THÁNG HIỆN TẠI của 1 Sales Staff (0 nếu chưa được đặt mục tiêu).</summary>
        Task<(decimal Target, decimal Revenue)> GetCurrentMonthTargetAndRevenueAsync(Guid? salesStaffId);
    }
}
