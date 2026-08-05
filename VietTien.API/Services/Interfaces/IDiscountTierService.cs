using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface IDiscountTierService
    {
        Task<List<DiscountTierDto>> GetAllAsync();
        Task<DiscountTierDto> GetByIdAsync(Guid id);
        Task<DiscountTierDto> CreateAsync(CreateDiscountTierRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
        Task<DiscountTierDto> UpdateAsync(Guid id, UpdateDiscountTierRequest request, Guid actorUserId, string actorEmail, string? ipAddress);

        // Dùng bởi OrderService.CalculateDiscountAsync (checkout thật) — tier IsActive khớp amount, MinAmount cao nhất thắng.
        Task<decimal> GetApplicableDiscountPercentAsync(decimal amount);
    }
}
