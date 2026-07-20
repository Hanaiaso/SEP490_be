using VietTien.API.DTOs.PurchaseOrder;

namespace VietTien.API.Services.Interfaces
{
    public interface IGoodsReceiptService
    {
        Task<GoodsReceiptDto> CreateFromPOAsync(Guid poId, Guid warehouseStaffId, CreateGoodsReceiptRequest request);
        Task<GoodsReceiptDto> PostReceiptAsync(Guid id, Guid warehouseStaffId);
        Task<IEnumerable<GoodsReceiptDto>> GetByPurchaseOrderIdAsync(Guid poId);
    }
}
