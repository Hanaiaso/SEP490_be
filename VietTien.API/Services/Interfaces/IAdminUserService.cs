using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;

namespace VietTien.API.Services.Interfaces
{
    public interface IAdminUserService
    {
        Task<PagedResultDto<AdminUserDto>> SearchAsync(AdminUserQueryDto query);
        Task<AdminUserDto> GetByIdAsync(Guid id);
        Task<AdminUserDto> CreateStaffAsync(CreateStaffUserRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
        Task<AdminUserDto> ChangeRoleAsync(Guid userId, ChangeUserRoleRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
        Task<AdminUserDto> SetActiveStatusAsync(Guid userId, SetUserActiveStatusRequest request, Guid actorUserId, string actorEmail, string? ipAddress);

        // P2-7: đăng xuất từ xa (thu hồi refresh token hiện tại) — UC-55
        Task<AdminUserDto> RevokeSessionAsync(Guid userId, RevokeSessionRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
    }
}
