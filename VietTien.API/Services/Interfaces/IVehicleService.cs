using VietTien.API.DTOs.Admin;

namespace VietTien.API.Services.Interfaces
{
    public interface IVehicleService
    {
        Task<List<VehicleDto>> GetAllAsync();
        Task<VehicleDto> GetByIdAsync(Guid id);
        Task<VehicleDto> CreateAsync(CreateVehicleRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
        Task<VehicleDto> UpdateAsync(Guid id, UpdateVehicleRequest request, Guid actorUserId, string actorEmail, string? ipAddress);
    }
}
