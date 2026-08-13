using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IInventoryCountSessionService
    {
        Task<List<InventoryCountSessionDto>> GetListAsync(Guid callerId, SystemRole callerRole, Guid? warehouseId, string? status);
        Task<InventoryCountSessionDto> GetByIdAsync(Guid id);
        Task<InventoryCountSessionDto> OpenAsync(Guid staffId, OpenInventoryCountSessionRequest request);
        Task<InventoryCountSessionDto> RecordItemCountAsync(Guid sessionId, Guid itemId, RecordCountItemRequest request);
        Task<CloseInventoryCountSessionResultDto> CloseAsync(Guid sessionId, Guid staffId);
    }
}
