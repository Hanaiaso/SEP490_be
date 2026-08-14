using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AdminUserService : IAdminUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;
        private readonly INotificationService _notificationService;

        public AdminUserService(ApplicationDbContext context, IAuditLogService auditLogService, INotificationService notificationService)
        {
            _context = context;
            _auditLogService = auditLogService;
            _notificationService = notificationService;
        }

        public async Task<PagedResultDto<AdminUserDto>> SearchAsync(AdminUserQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

            var source = _context.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Role))
            {
                if (!Enum.TryParse<SystemRole>(query.Role, true, out var role))
                    throw new Exception($"Role '{query.Role}' không hợp lệ.");
                source = source.Where(u => u.Role == role);
            }

            if (query.IsActive.HasValue)
                source = source.Where(u => u.IsActive == query.IsActive.Value);

            if (!string.IsNullOrWhiteSpace(query.SearchQuery))
            {
                var q = query.SearchQuery.Trim();
                source = source.Where(u =>
                    u.FullName.Contains(q) ||
                    u.Email.Contains(q) ||
                    u.PhoneNumber.Contains(q));
            }

            var totalCount = await source.CountAsync();

            var entities = await source
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResultDto<AdminUserDto>
            {
                Items = entities.Select(ToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }

        public async Task<AdminUserDto> GetByIdAsync(Guid id)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) throw new KeyNotFoundException("Không tìm thấy người dùng.");
            return ToDto(user);
        }

        public async Task<AdminUserDto> CreateStaffAsync(CreateStaffUserRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            var role = ParseAssignableRole(request.Role);

            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new Exception("Họ tên không được để trống.");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
                throw new Exception("Mật khẩu phải có ít nhất 6 ký tự.");

            var normalizedEmail = request.Email.Trim().ToLower();
            if (await _context.Users.AnyAsync(u => u.Email == normalizedEmail))
                throw new InvalidOperationException($"Email '{normalizedEmail}' đã được sử dụng.");

            // So khớp trên giá trị ĐÃ trim, khớp đúng với giá trị thực sự được lưu ở dưới
            // (PhoneNumber = request.PhoneNumber?.Trim()) — nếu so trên giá trị thô, SĐT có khoảng
            // trắng thừa (vd " 0912345678") sẽ lọt qua check này rồi mới đụng unique index lúc
            // SaveChanges, gây lỗi 500 chung chung thay vì thông báo rõ ràng.
            var normalizedPhone = request.PhoneNumber?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedPhone) &&
                await _context.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone))
                throw new InvalidOperationException($"Số điện thoại '{normalizedPhone}' đã được sử dụng.");

            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = normalizedPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = role,
                IsActive = true,
                // Tài khoản nội bộ do Admin tạo trực tiếp -> coi như đã xác minh, không cần luồng OTP công khai.
                IsEmailVerified = true,
                IsPhoneVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var dto = ToDto(user);

            await _auditLogService.LogAsync(
                entityName: "User",
                entityId: user.Id.ToString(),
                action: "CREATE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: null,
                after: dto,
                reason: "Admin tạo tài khoản nhân viên",
                ipAddress: ipAddress);

            return dto;
        }

        public async Task<AdminUserDto> ChangeRoleAsync(Guid userId, ChangeUserRoleRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            var newRole = ParseAssignableRole(request.NewRole);

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new Exception("Vui lòng nhập lý do đổi vai trò để phục vụ audit.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new KeyNotFoundException("Không tìm thấy người dùng.");

            if (user.Role == SystemRole.Customer || user.Role == SystemRole.Guest)
                throw new InvalidOperationException("Không thể đổi vai trò của tài khoản Khách hàng qua chức năng này.");

            if (userId == actorUserId && newRole != SystemRole.Admin)
                throw new InvalidOperationException("Không thể tự thu hồi quyền Admin của chính mình.");

            var beforeRole = user.Role;
            if (beforeRole == newRole) return ToDto(user);

            if (beforeRole == SystemRole.SalesStaff && newRole != SystemRole.SalesStaff)
            {
                var hasAssignedCustomers = await _context.CustomerProfiles
                    .AnyAsync(cp => cp.AssignedSalesStaffId == userId);
                if (hasAssignedCustomers)
                    throw new InvalidOperationException(
                        "Nhân viên này đang phụ trách khách hàng — cần bàn giao khách trước khi đổi vai trò.");
            }

            user.Role = newRole;
            // Đổi vai trò = đổi quyền hạn -> bắt buộc đăng nhập lại
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            await _context.SaveChangesAsync();

            var dto = ToDto(user);

            await _auditLogService.LogAsync(
                entityName: "User",
                entityId: user.Id.ToString(),
                action: "ROLE_CHANGE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: new { Role = beforeRole.ToString() },
                after: new { Role = newRole.ToString() },
                reason: request.Reason,
                ipAddress: ipAddress);

            // Tài khoản đã đổi vai trò và commit thành công ở trên -> lỗi gửi notification không
            // được làm fail request đổi vai trò, chỉ log để theo dõi.
            try
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_31_RoleChanged,
                    user.Id,
                    "Vai trò tài khoản đã thay đổi",
                    $"Tài khoản của bạn đã được đổi từ vai trò {beforeRole} sang {newRole}. Lý do: {request.Reason}. Vui lòng đăng nhập lại.",
                    user.Id,
                    "User");
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[AdminUserService] Error sending role change notification: {notifyEx.Message}");
            }

            return dto;
        }

        public async Task<AdminUserDto> SetActiveStatusAsync(Guid userId, SetUserActiveStatusRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new Exception("Vui lòng nhập lý do khóa/mở tài khoản để phục vụ audit.");

            if (userId == actorUserId && !request.IsActive)
                throw new InvalidOperationException("Không thể tự khóa tài khoản của chính mình.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new KeyNotFoundException("Không tìm thấy người dùng.");

            var before = user.IsActive;
            if (before == request.IsActive) return ToDto(user);

            if (!request.IsActive && user.Role == SystemRole.SalesStaff)
            {
                var otherActiveSalesCount = await _context.Users
                    .CountAsync(u => u.Id != userId && u.Role == SystemRole.SalesStaff && u.IsActive);
                if (otherActiveSalesCount == 0)
                    throw new InvalidOperationException(
                        "Không thể khóa: đây là nhân viên Sales đang hoạt động cuối cùng, không còn ai nhận khách mới.");
            }

            user.IsActive = request.IsActive;
            if (!request.IsActive)
            {
                // Khóa tài khoản -> buộc đăng xuất khỏi mọi phiên hiện tại
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
            }

            await _context.SaveChangesAsync();

            var dto = ToDto(user);

            await _auditLogService.LogAsync(
                entityName: "User",
                entityId: user.Id.ToString(),
                action: "STATUS_CHANGE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: new { IsActive = before },
                after: new { IsActive = request.IsActive },
                reason: request.Reason,
                ipAddress: ipAddress);

            // Tài khoản đã đổi trạng thái và commit thành công ở trên -> lỗi gửi notification không
            // được làm fail request khóa/mở tài khoản, chỉ log để theo dõi.
            try
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_30_AccountStatusChanged,
                    user.Id,
                    request.IsActive ? "Tài khoản đã được mở lại" : "Tài khoản đã bị khóa",
                    request.IsActive
                        ? $"Tài khoản của bạn đã được mở lại. Lý do: {request.Reason}."
                        : $"Tài khoản của bạn đã bị khóa. Lý do: {request.Reason}. Vui lòng liên hệ Admin nếu cần hỗ trợ.",
                    user.Id,
                    "User");
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[AdminUserService] Error sending account status change notification: {notifyEx.Message}");
            }

            return dto;
        }

        public async Task<AdminUserDto> RevokeSessionAsync(Guid userId, RevokeSessionRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new Exception("Vui lòng nhập lý do đăng xuất từ xa để phục vụ audit.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new KeyNotFoundException("Không tìm thấy người dùng.");

            var hadActiveSession = user.RefreshToken != null && user.RefreshTokenExpiryTime > DateTime.UtcNow;
            if (!hadActiveSession) return ToDto(user);

            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            await _context.SaveChangesAsync();

            var dto = ToDto(user);

            await _auditLogService.LogAsync(
                entityName: "User",
                entityId: user.Id.ToString(),
                action: "SESSION_REVOKE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: new { HasActiveSession = true },
                after: new { HasActiveSession = false },
                reason: request.Reason,
                ipAddress: ipAddress);

            // Phiên đã bị thu hồi và commit thành công ở trên -> lỗi gửi notification không
            // được làm fail request đăng xuất từ xa, chỉ log để theo dõi.
            try
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_38_SessionRevoked,
                    user.Id,
                    "Phiên đăng nhập đã bị thu hồi",
                    $"Quản trị viên đã đăng xuất bạn khỏi hệ thống từ xa. Lý do: {request.Reason}. Vui lòng đăng nhập lại.",
                    user.Id,
                    "User");
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[AdminUserService] Error sending session revoked notification: {notifyEx.Message}");
            }

            return dto;
        }

        private static SystemRole ParseAssignableRole(string role)
        {
            if (!AdminAssignableRoles.Values.Contains(role, StringComparer.OrdinalIgnoreCase))
                throw new Exception($"Role '{role}' không hợp lệ hoặc không được phép gán qua Admin. Cho phép: {string.Join(", ", AdminAssignableRoles.Values)}.");

            return Enum.Parse<SystemRole>(role, true);
        }

        private static AdminUserDto ToDto(User u) => new()
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            Role = u.Role.ToString(),
            IsActive = u.IsActive,
            IsEmailVerified = u.IsEmailVerified,
            CreatedAt = u.CreatedAt,
            HasActiveSession = u.RefreshToken != null && u.RefreshTokenExpiryTime > DateTime.UtcNow,
            SessionExpiresAt = u.RefreshTokenExpiryTime
        };
    }
}
