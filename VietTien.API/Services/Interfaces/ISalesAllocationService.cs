using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Sales;

namespace VietTien.API.Services.Interfaces
{
    public interface ISalesAllocationService
    {
        // Khách hàng của tôi (SAL-02)
        Task<PagedResultDto<MyCustomerListItemDto>> GetMyCustomersAsync(Guid staffId, MyCustomerQueryDto query);
        Task<MyCustomerDetailDto> GetMyCustomerDetailAsync(Guid staffId, Guid customerProfileId);
        Task UpdateCustomerNoteAsync(Guid staffId, Guid customerProfileId, string? note);

        // Quản lý Round-robin (MGR-02)
        Task<RoundRobinStateDto> GetRoundRobinStateAsync();
        Task<RoundRobinStateDto> UpdateRoundRobinAsync(Guid managerId, UpdateRoundRobinRequest request);
        Task<RoundRobinAssignResultDto> AssignUnassignedCustomersAsync(Guid managerId);

        // Hook gán tự động khi khách đăng ký/xác minh (WF-01)
        // Ưu tiên: khách cũ (RETURNING_CUSTOMER) → referral (REFERRAL) → Round-robin (ROUND_ROBIN)
        Task AutoAssignCustomerAsync(Guid userId);

        // Tra mã giới thiệu (ReferralCode hoặc email của Sale). Trả về lỗi nếu không hợp lệ/trùng (WF-01 A1, A2)
        Task<(Guid? StaffId, string? Error)> ResolveReferralStaffAsync(string code);

        // Tạo sẵn CustomerProfile khi đăng ký (lưu MST để nhận diện khách cũ)
        Task EnsureCustomerProfileAsync(Guid userId, string? taxCode);
    }
}
