using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;

namespace VietTien.API.Services.Interfaces
{
    public interface IAuditLogService
    {
        // before/after nên là DTO "sạch" (không chứa PasswordHash/OtpCode/Token...).
        // Service vẫn tự động redact thêm 1 lớp nữa dựa trên tên field làm lưới an toàn cuối cùng.
        Task LogAsync(
            string entityName,
            string entityId,
            string action,
            Guid? actorUserId,
            string? actorEmail,
            string? actorRole,
            object? before,
            object? after,
            string? reason = null,
            string? ipAddress = null);

        Task<PagedResultDto<AuditLogDto>> SearchAsync(AuditLogQueryDto query);

        Task<byte[]> ExportCsvAsync(AuditLogQueryDto query);
    }
}
