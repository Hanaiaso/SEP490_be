using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    /// <inheritdoc cref="IWarehouseAccessGuard"/>
    public class WarehouseAccessGuard : IWarehouseAccessGuard
    {
        // Mã lỗi theo SRS NAC-05, dùng làm AuditLog.Action để tra cứu mọi lần vi phạm phạm vi kho.
        public const string ForbiddenAuditAction = "WAREHOUSE_ACTION_FORBIDDEN";

        private readonly ApplicationDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WarehouseAccessGuard> _logger;

        public WarehouseAccessGuard(
            ApplicationDbContext context,
            IServiceScopeFactory scopeFactory,
            ILogger<WarehouseAccessGuard> logger)
        {
            _context = context;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task EnsureWarehouseAccessAsync(
            Guid staffId,
            Guid warehouseId,
            string action,
            string entityName,
            string? entityId = null)
        {
            var staff = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId);

            // Giữ đúng semantics của 4 call-site đã chạy ổn định trước đây:
            //  - staff == null (token trỏ tới user không còn trong DB) -> không chặn ở tầng này,
            //    [Authorize] đã lọc trước và các luồng khác vẫn dựa trên hành vi cũ.
            //  - chỉ WarehouseStaff bị giới hạn phạm vi; CEO/Admin/SalesManager thao tác xuyên kho.
            //  - AssignedWarehouseId == null -> luôn lệch -> chặn (xem chú thích ApplicationDbContext.cs:1071).
            if (staff == null || staff.Role != SystemRole.WarehouseStaff) return;
            if (staff.AssignedWarehouseId == warehouseId) return;

            await WriteForbiddenAuditAsync(staff, warehouseId, action, entityName, entityId);

            throw new UnauthorizedAccessException($"Bạn không có quyền {action} của kho này.");
        }

        public async Task<Guid?> GetScopedWarehouseIdAsync(Guid callerId)
        {
            var staff = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId);
            if (staff == null || staff.Role != SystemRole.WarehouseStaff) return null;

            // WarehouseStaff chưa được gán kho -> AssignedWarehouseId null -> bộ lọc so sánh với null
            // sẽ không khớp bản ghi nào, tức là không thấy gì. Đúng hướng "đóng mặc định".
            return staff.AssignedWarehouseId ?? Guid.Empty;
        }

        /// <summary>
        /// Ghi AuditLog bằng DbContext riêng (scope mới) để hoàn toàn tách khỏi context của caller:
        /// caller có thể đang mở transaction sắp rollback, hoặc đang track entity dở dang mà ta
        /// tuyệt đối không được flush hộ. Lỗi ghi audit không được làm hỏng phản hồi 403.
        /// </summary>
        private async Task WriteForbiddenAuditAsync(
            User staff, Guid warehouseId, string action, string entityName, string? entityId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var auditContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                auditContext.AuditLogs.Add(new AuditLog
                {
                    EntityName = entityName,
                    EntityId = entityId ?? warehouseId.ToString(),
                    Action = ForbiddenAuditAction,
                    ActorUserId = staff.Id,
                    ActorEmail = staff.Email,
                    ActorRole = staff.Role.ToString(),
                    Reason = $"Thao tác '{action}' trên kho {warehouseId} " +
                             $"trong khi kho được phân công là {staff.AssignedWarehouseId?.ToString() ?? "(chưa gán)"}.",
                    CreatedAt = DateTime.UtcNow
                });

                await auditContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Không ghi được AuditLog {Action} cho user {StaffId} trên kho {WarehouseId}.",
                    ForbiddenAuditAction, staff.Id, warehouseId);
            }
        }
    }
}
