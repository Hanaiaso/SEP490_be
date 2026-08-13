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

        // INV-01: kiểm kê kho 2 bước (snapshot lý thuyết -> ghi số đếm thực tế), tách biệt với shift-count
        Task<InventoryCountSessionDto> CreateCountSessionAsync(Guid staffId, Guid warehouseId);
        Task<InventoryCountSessionDto> LockTheoreticalAsync(Guid sessionId);
        Task<InventoryCountSessionDto> RecordCountLineAsync(Guid sessionId, RecordCountLineRequestDto dto);
    }
}
