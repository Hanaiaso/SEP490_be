using VietTien.API.DTOs.Supplier;

namespace VietTien.API.Services.Interfaces
{
    public interface ISupplierService
    {
        Task<IEnumerable<SupplierDto>> GetAllAsync();
        Task<SupplierDto> GetByIdAsync(Guid id);
        Task<SupplierDto> CreateAsync(CreateSupplierRequest request);
        Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest request);
    }
}
