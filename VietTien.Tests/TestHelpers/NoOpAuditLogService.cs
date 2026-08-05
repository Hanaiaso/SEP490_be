using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.Services.Interfaces;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// IAuditLogService không làm gì — dùng khi test cần một service THẬT (vd SystemConfigService
    /// để job đọc cấu hình) nhưng bản thân audit không phải đối tượng kiểm tra.
    /// Khi cần verify audit thì dùng Mock&lt;IAuditLogService&gt; thay cho lớp này.
    /// </summary>
    public class NoOpAuditLogService : IAuditLogService
    {
        public Task LogAsync(
            string entityName, string entityId, string action,
            Guid? actorUserId, string? actorEmail, string? actorRole,
            object? before, object? after, string? reason = null, string? ipAddress = null)
            => Task.CompletedTask;

        public Task<PagedResultDto<AuditLogDto>> SearchAsync(AuditLogQueryDto query)
            => Task.FromResult(new PagedResultDto<AuditLogDto>());

        public Task<byte[]> ExportCsvAsync(AuditLogQueryDto query)
            => Task.FromResult(Array.Empty<byte>());
    }
}
