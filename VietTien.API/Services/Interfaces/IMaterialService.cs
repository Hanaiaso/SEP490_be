using VietTien.API.DTOs.Material;

namespace VietTien.API.Services.Interfaces
{
    public interface IMaterialService
    {
        Task<IEnumerable<MaterialDto>> GetAllAsync(string? search = null);
        Task<MaterialDto> GetByIdAsync(Guid id);
        Task<MaterialDto> CreateAsync(CreateMaterialDto dto);
        Task<MaterialDto> UpdateAsync(Guid id, UpdateMaterialDto dto);
        Task DeleteAsync(Guid id);
    }
}
