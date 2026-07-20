using VietTien.API.DTOs.PurchaseOrder;

namespace VietTien.API.Services.Interfaces
{
    public interface IPurchaseOrderService
    {
        Task<PurchaseOrderDto> CreateAsync(Guid ceoId, CreatePurchaseOrderRequest request);
        Task<PurchaseOrderDto> ImportFromExcelAsync(Microsoft.AspNetCore.Http.IFormFile file, Guid ceoId);
        Task<PurchaseOrderDto> ImportFromImageAsync(Microsoft.AspNetCore.Http.IFormFile file, Guid ceoId);
        Task<PurchaseOrderDto> UpdateDraftAsync(Guid id, Guid ceoId, CreatePurchaseOrderRequest request);
        Task<PurchaseOrderDto> IssueAsync(Guid id, Guid ceoId);
        Task<PurchaseOrderDto> SendToWarehouseAsync(Guid id, Guid ceoId);
        Task<IEnumerable<PurchaseOrderListDto>> GetAllAsync(string? statusFilter);
        Task<PurchaseOrderDto> GetByIdAsync(Guid id);
        Task<PurchaseOrderDto> CancelAsync(Guid id, Guid ceoId);
        Task<PurchaseOrderDto> ResolveDiscrepancyAsync(Guid id, Guid ceoId, DiscrepancyResolutionRequest request);
        Task<PurchaseOrderDto> ClosePurchaseOrderAsync(Guid id, Guid ceoId);
    }
}
