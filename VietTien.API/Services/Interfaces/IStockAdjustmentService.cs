using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IStockAdjustmentService
    {
        Task<List<StockAdjustmentDto>> GetListAsync(Guid callerId, SystemRole callerRole, string? status);
        Task<StockAdjustmentDto> GetByIdAsync(Guid id);
        Task<StockAdjustmentDto> CreateAsync(Guid staffId, CreateStockAdjustmentRequest request);
        Task<StockAdjustmentDto> DecideAsync(Guid adjustmentId, Guid ceoId, StockAdjustmentDecisionRequest request);
    }
}
