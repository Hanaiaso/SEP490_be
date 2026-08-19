using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ISystemConfigService
    {
        Task<List<SystemConfigDto>> GetAllWithEffectiveValuesAsync();

        /// <summary>Chỉ trả các config có OwnerLevel chứa <paramref name="ownerToken"/> (vd "CEO") và không phải secret —
        /// dùng cho các portal ngoài Admin (vd CEO) chỉ được thấy đúng phần cấu hình họ sở hữu.</summary>
        Task<List<SystemConfigDto>> GetForOwnerAsync(string ownerToken);

        Task<string?> GetEffectiveValueAsync(string key, DateTime? asOf = null);
        Task<List<SystemConfigVersionDto>> GetHistoryAsync(string key);

        /// <summary>Khi <paramref name="requiredOwnerToken"/> khác null: chặn (UnauthorizedAccessException)
        /// nếu config không thuộc OwnerLevel đó hoặc là secret — dùng cho các portal ngoài Admin.</summary>
        Task<SystemConfigDto> SetValueAsync(string key, UpdateSystemConfigRequest request, Guid actorUserId, string actorEmail, string? ipAddress, string? requiredOwnerToken = null);
    }
}
