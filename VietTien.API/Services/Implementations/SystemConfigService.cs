using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SystemConfigService : ISystemConfigService
    {
        // Giá trị hiển thị thay cho secret thật (API key/password...) trong mọi response trả cho FE
        // và trong audit log — không bao giờ trả plaintext hay bản mã hoá ra ngoài service này.
        private const string SecretMask = "••••••••";

        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;
        private readonly IDataProtector _protector;

        public SystemConfigService(ApplicationDbContext context, IAuditLogService auditLogService, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _auditLogService = auditLogService;
            _protector = dataProtectionProvider.CreateProtector("VietTien.SystemConfig.Secrets");
        }

        public async Task<List<SystemConfigDto>> GetAllWithEffectiveValuesAsync()
        {
            var configs = await _context.SystemConfigs.AsNoTracking().ToListAsync();
            var result = new List<SystemConfigDto>();

            foreach (var config in configs)
            {
                var effective = await GetEffectiveVersionAsync(config.Key);
                var versionCount = await _context.SystemConfigVersions.CountAsync(v => v.ConfigKey == config.Key);

                result.Add(new SystemConfigDto
                {
                    Key = config.Key,
                    ValueType = config.ValueType.ToString(),
                    Description = config.Description,
                    Unit = config.Unit,
                    OwnerLevel = config.OwnerLevel,
                    IsActive = config.IsActive,
                    IsSecret = config.IsSecret,
                    // Secret: không bao giờ giải mã trả ra API — chỉ báo "đã cấu hình" bằng mask cố định.
                    EffectiveValue = config.IsSecret
                        ? (string.IsNullOrEmpty(effective?.Value) ? null : SecretMask)
                        : effective?.Value,
                    EffectiveDate = effective?.EffectiveDate,
                    VersionCount = versionCount
                });
            }

            return result.OrderBy(c => c.Key).ToList();
        }

        // Dùng nội bộ bởi các service khác (OrderService, AuthService, eSmsService...) để đọc giá trị
        // THẬT (đã giải mã nếu là secret) — không phải endpoint public, không đi qua mask.
        public async Task<string?> GetEffectiveValueAsync(string key, DateTime? asOf = null)
        {
            var version = await GetEffectiveVersionAsync(key, asOf);
            if (version == null) return null;

            var isSecret = await _context.SystemConfigs.AsNoTracking()
                .Where(c => c.Key == key)
                .Select(c => c.IsSecret)
                .FirstOrDefaultAsync();

            if (!isSecret) return version.Value;

            try
            {
                return _protector.Unprotect(version.Value);
            }
            catch (CryptographicException)
            {
                // Giá trị lưu không giải mã được (vd đổi data-protection key, hoặc dữ liệu cũ chưa mã hoá)
                // -> coi như chưa cấu hình, để caller tự fallback về appsettings thay vì crash.
                return null;
            }
        }

        public async Task<List<SystemConfigVersionDto>> GetHistoryAsync(string key)
        {
            var config = await _context.SystemConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key);
            if (config == null) throw new KeyNotFoundException($"Không tìm thấy tham số cấu hình '{key}'.");

            var now = DateTime.UtcNow;
            var currentEffective = await GetEffectiveVersionAsync(key, now);

            var versions = await _context.SystemConfigVersions
                .AsNoTracking()
                .Where(v => v.ConfigKey == key)
                .OrderByDescending(v => v.EffectiveDate)
                .ThenByDescending(v => v.CreatedAt)
                .ToListAsync();

            // Lịch sử của key secret cũng phải mask từng phiên bản — không riêng gì giá trị hiệu lực hiện tại.
            return versions.Select(v => new SystemConfigVersionDto
            {
                Id = v.Id,
                ConfigKey = v.ConfigKey,
                Value = config.IsSecret ? SecretMask : v.Value,
                EffectiveDate = v.EffectiveDate,
                ActorUserId = v.ActorUserId,
                ActorEmail = v.ActorEmail,
                ChangeReason = v.ChangeReason,
                CreatedAt = v.CreatedAt,
                IsCurrentlyEffective = currentEffective != null && v.Id == currentEffective.Id
            }).ToList();
        }

        public async Task<SystemConfigDto> SetValueAsync(string key, UpdateSystemConfigRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            var config = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (config == null) throw new KeyNotFoundException($"Không tìm thấy tham số cấu hình '{key}'.");

            if (string.IsNullOrWhiteSpace(request.Value))
                throw new Exception("Giá trị cấu hình không được để trống.");

            ValidateValueType(config.ValueType, request.Value);

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new Exception("Vui lòng nhập lý do thay đổi cấu hình để phục vụ audit.");

            if (request.EffectiveDate.HasValue && request.EffectiveDate.Value.ToUniversalTime() < DateTime.UtcNow)
                throw new InvalidOperationException("Không được đặt ngày hiệu lực vào quá khứ (không cho phép thay đổi hồi tố).");

            var beforeVersion = await GetEffectiveVersionAsync(key, DateTime.UtcNow);

            // Secret: mã hoá trước khi lưu, không bao giờ ghi plaintext xuống DB.
            var storedValue = config.IsSecret ? _protector.Protect(request.Value.Trim()) : request.Value.Trim();

            var newVersion = new SystemConfigVersion
            {
                ConfigKey = key,
                Value = storedValue,
                EffectiveDate = (request.EffectiveDate ?? DateTime.UtcNow).ToUniversalTime(),
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                ChangeReason = request.Reason,
                CreatedAt = DateTime.UtcNow
            };

            _context.SystemConfigVersions.Add(newVersion);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                entityName: "SystemConfig",
                entityId: key,
                action: "CONFIG_CHANGE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: beforeVersion == null ? null : new { Value = config.IsSecret ? SecretMask : beforeVersion.Value, beforeVersion.EffectiveDate },
                after: new { Value = config.IsSecret ? SecretMask : newVersion.Value, newVersion.EffectiveDate },
                reason: request.Reason,
                ipAddress: ipAddress);

            var effective = await GetEffectiveVersionAsync(key);
            var versionCount = await _context.SystemConfigVersions.CountAsync(v => v.ConfigKey == key);

            return new SystemConfigDto
            {
                Key = config.Key,
                ValueType = config.ValueType.ToString(),
                Description = config.Description,
                Unit = config.Unit,
                OwnerLevel = config.OwnerLevel,
                IsActive = config.IsActive,
                IsSecret = config.IsSecret,
                EffectiveValue = config.IsSecret
                    ? (string.IsNullOrEmpty(effective?.Value) ? null : SecretMask)
                    : effective?.Value,
                EffectiveDate = effective?.EffectiveDate,
                VersionCount = versionCount
            };
        }

        private async Task<SystemConfigVersion?> GetEffectiveVersionAsync(string key, DateTime? asOf = null)
        {
            var cutoff = asOf ?? DateTime.UtcNow;

            return await _context.SystemConfigVersions
                .AsNoTracking()
                .Where(v => v.ConfigKey == key && v.EffectiveDate <= cutoff)
                .OrderByDescending(v => v.EffectiveDate)
                .ThenByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();
        }

        private static void ValidateValueType(SystemConfigValueType type, string value)
        {
            switch (type)
            {
                case SystemConfigValueType.Int:
                    if (!int.TryParse(value, out _))
                        throw new Exception("Giá trị phải là số nguyên.");
                    break;
                case SystemConfigValueType.Decimal:
                    if (!decimal.TryParse(value, out _))
                        throw new Exception("Giá trị phải là số thập phân.");
                    break;
                case SystemConfigValueType.Bool:
                    if (!bool.TryParse(value, out _))
                        throw new Exception("Giá trị phải là true/false.");
                    break;
                case SystemConfigValueType.String:
                default:
                    break;
            }
        }
    }
}
