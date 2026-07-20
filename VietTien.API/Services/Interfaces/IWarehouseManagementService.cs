using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IWarehouseManagementService
    {
        Task<List<WarehouseDto>> GetAllWarehousesAsync();
        Task<WarehouseDto> GetWarehouseByIdAsync(Guid id);
        Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto);
        Task<WarehouseDto> UpdateWarehouseAsync(Guid id, UpdateWarehouseDto dto);
        Task DeleteWarehouseAsync(Guid id);
    }
}
