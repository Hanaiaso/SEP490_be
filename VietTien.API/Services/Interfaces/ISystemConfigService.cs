using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface ISystemConfigService
    {
        Task<List<SystemConfigDto>> GetAllWithEffectiveValuesAsync();
        Task<string?> GetEffectiveValueAsync(string key, DateTime? asOf = null);
        Task<List<SystemConfigVersionDto>> GetHistoryAsync(string key);
        Task<SystemConfigDto> SetValueAsync(string key, UpdateSystemConfigRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
    }
}
