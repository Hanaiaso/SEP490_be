using VietTien.API.DTOs.ProductPriceUpdate;

namespace VietTien.API.Services.Interfaces
{
    public interface IProductPriceUpdateService
    {
        Task<ProductPriceUpdateOrderDto> ProposeAsync(Guid ceoUserId, CreateProductPriceUpdateOrderRequest request);
        Task<ProductPriceUpdateOrderDto> AssignAndNotifyAsync(Guid orderId, Guid managerId, AssignPriceUpdateOrderRequest request);
        Task<ProductPriceUpdateOrderDto> ExecuteAsync(Guid orderId, Guid staffId);
        Task<ProductPriceUpdateOrderDto> CancelAsync(Guid orderId, Guid actorUserId, string actorRole, CancelPriceUpdateOrderRequest request);
        Task<ProductPriceUpdateOrderDto> GetByIdAsync(Guid orderId);
        Task<IEnumerable<ProductPriceUpdateOrderDto>> GetAllAsync();
        Task<IEnumerable<ProductPriceUpdateOrderDto>> GetPendingForManagerAsync();
        Task<IEnumerable<ProductPriceUpdateOrderDto>> GetPendingForStaffAsync(Guid staffId);
    }
}
