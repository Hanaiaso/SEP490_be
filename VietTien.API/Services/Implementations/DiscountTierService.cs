using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class DiscountTierService : IDiscountTierService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public DiscountTierService(ApplicationDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        public async Task<List<DiscountTierDto>> GetAllAsync()
        {
            var tiers = await _context.DiscountTiers.AsNoTracking().OrderBy(t => t.MinAmount).ToListAsync();
            return tiers.Select(ToDto).ToList();
        }

        public async Task<DiscountTierDto> GetByIdAsync(Guid id)
        {
            var tier = await _context.DiscountTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tier == null) throw new KeyNotFoundException("Không tìm thấy khung chiết khấu.");
            return ToDto(tier);
        }

        public async Task<DiscountTierDto> CreateAsync(CreateDiscountTierRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            Validate(request.MinAmount, request.MaxAmount, request.DiscountPercent);
            await EnsureNoOverlapAsync(request.MinAmount, request.MaxAmount);

            var tier = new DiscountTier
            {
                MinAmount = request.MinAmount,
                MaxAmount = request.MaxAmount,
                DiscountPercent = request.DiscountPercent,
                Description = request.Description,
                IsActive = true
            };

            _context.DiscountTiers.Add(tier);
            await _context.SaveChangesAsync();

            var dto = ToDto(tier);

            await _auditLogService.LogAsync(
                entityName: "DiscountTier",
                entityId: tier.Id.ToString(),
                action: "CREATE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: null,
                after: dto,
                reason: "Admin tạo khung chiết khấu mới",
                ipAddress: ipAddress);

            return dto;
        }

        public async Task<DiscountTierDto> UpdateAsync(Guid id, UpdateDiscountTierRequest request, Guid actorUserId, string actorEmail, string? ipAddress)
        {
            Validate(request.MinAmount, request.MaxAmount, request.DiscountPercent);
            if (request.IsActive)
                await EnsureNoOverlapAsync(request.MinAmount, request.MaxAmount, excludeId: id);

            var tier = await _context.DiscountTiers.FirstOrDefaultAsync(t => t.Id == id);
            if (tier == null) throw new KeyNotFoundException("Không tìm thấy khung chiết khấu.");

            var before = ToDto(tier);

            tier.MinAmount = request.MinAmount;
            tier.MaxAmount = request.MaxAmount;
            tier.DiscountPercent = request.DiscountPercent;
            tier.IsActive = request.IsActive;
            tier.Description = request.Description;

            await _context.SaveChangesAsync();

            var after = ToDto(tier);

            await _auditLogService.LogAsync(
                entityName: "DiscountTier",
                entityId: tier.Id.ToString(),
                action: "UPDATE",
                actorUserId: actorUserId,
                actorEmail: actorEmail,
                actorRole: "Admin",
                before: before,
                after: after,
                reason: "Admin cập nhật khung chiết khấu",
                ipAddress: ipAddress);

            return after;
        }

        public async Task<decimal> GetApplicableDiscountPercentAsync(decimal amount)
        {
            var tier = await _context.DiscountTiers
                .AsNoTracking()
                .Where(t => t.IsActive && amount >= t.MinAmount && (t.MaxAmount == null || amount < t.MaxAmount))
                .OrderByDescending(t => t.MinAmount)
                .FirstOrDefaultAsync();

            return tier?.DiscountPercent ?? 0m;
        }

        private async Task EnsureNoOverlapAsync(decimal minAmount, decimal? maxAmount, Guid? excludeId = null)
        {
            var overlaps = await _context.DiscountTiers
                .Where(t => t.IsActive && (excludeId == null || t.Id != excludeId.Value))
                .Where(t => (t.MaxAmount == null || minAmount < t.MaxAmount.Value) &&
                            (maxAmount == null || t.MinAmount < maxAmount.Value))
                .AnyAsync();

            if (overlaps)
                throw new InvalidOperationException("Khoảng giá trị bị chồng lấn với một khung chiết khấu khác đang hiệu lực.");
        }

        private static void Validate(decimal minAmount, decimal? maxAmount, decimal discountPercent)
        {
            if (minAmount < 0)
                throw new Exception("MinAmount không được âm.");

            if (maxAmount.HasValue && maxAmount.Value <= minAmount)
                throw new Exception("MaxAmount phải lớn hơn MinAmount (hoặc để trống nếu không giới hạn trên).");

            if (discountPercent < 0 || discountPercent > 1)
                throw new Exception("DiscountPercent phải trong khoảng 0 đến 1 (vd 0.05 = 5%).");
        }

        private static DiscountTierDto ToDto(DiscountTier t) => new()
        {
            Id = t.Id,
            MinAmount = t.MinAmount,
            MaxAmount = t.MaxAmount,
            DiscountPercent = t.DiscountPercent,
            IsActive = t.IsActive,
            Description = t.Description
        };
    }
}
