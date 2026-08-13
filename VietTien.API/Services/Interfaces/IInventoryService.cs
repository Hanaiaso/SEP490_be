using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IInventoryService
    {
        Task<PaginatedList<InventoryItemDto>> GetInventoryByWarehouseAsync(Guid warehouseId, string? search, int? minQty, int? maxQty, DateTime? fromDate, DateTime? toDate, int pageNumber, int pageSize);
        Task AdjustInventoryAsync(Guid inventoryId, int newQuantity, string? note, Guid staffId);
        Task<InventoryItemDto> AddProductToWarehouseAsync(AddInventoryRequest request, Guid staffId);
        Task<InventoryReportDto> GetInventoryReportAsync(Guid? warehouseId, DateTime? fromDate, DateTime? toDate);
        Task<List<SlowMovingItemDto>> GetSlowMovingItemsAsync(Guid? warehouseId, int days);
        Task<ShiftInventoryCountResultDto> SubmitShiftInventoryCountAsync(ShiftInventoryCountRequestDto request, Guid staffId);
        Task<List<LowStockAlertDto>> GetLowStockAlertsAsync();
    }
}
