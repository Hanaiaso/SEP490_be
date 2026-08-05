using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SystemConfigService : ISystemConfigService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public SystemConfigService(ApplicationDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
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
                    EffectiveValue = effective?.Value,
                    EffectiveDate = effective?.EffectiveDate,
                    VersionCount = versionCount
                });
            }

            return result.OrderBy(c => c.Key).ToList();
        }

        public async Task<string?> GetEffectiveValueAsync(string key, DateTime? asOf = null)
        {
            var version = await GetEffectiveVersionAsync(key, asOf);
            return version?.Value;
        }

        public async Task<List<SystemConfigVersionDto>> GetHistoryAsync(string key)
        {
            var configExists = await _context.SystemConfigs.AnyAsync(c => c.Key == key);
            if (!configExists) throw new KeyNotFoundException($"Không tìm thấy tham số cấu hình '{key}'.");

            var now = DateTime.UtcNow;
            var currentEffective = await GetEffectiveVersionAsync(key, now);

            var versions = await _context.SystemConfigVersions
                .AsNoTracking()
                .Where(v => v.ConfigKey == key)
                .OrderByDescending(v => v.EffectiveDate)
                .ThenByDescending(v => v.CreatedAt)
                .ToListAsync();

            return versions.Select(v => new SystemConfigVersionDto
            {
                Id = v.Id,
                ConfigKey = v.ConfigKey,
                Value = v.Value,
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

            var newVersion = new SystemConfigVersion
            {
                ConfigKey = key,
                Value = request.Value.Trim(),
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
                before: beforeVersion == null ? null : new { beforeVersion.Value, beforeVersion.EffectiveDate },
                after: new { newVersion.Value, newVersion.EffectiveDate },
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
                EffectiveValue = effective?.Value,
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
