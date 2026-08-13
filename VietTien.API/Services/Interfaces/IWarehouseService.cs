using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Services.Interfaces
{
    public interface IWarehouseService
    {
        Task<List<WarehouseOrderListDto>> GetOrdersForWarehouseAsync(string tabType, int pageNumber, int pageSize);
        Task<WarehouseOrderDetailDto> GetOrderDetailAsync(Guid orderId);
        
        // PickTask-specific methods
        Task<List<PickTaskDto>> GetPickTasksAsync(string tabType, int pageNumber, int pageSize);
        Task<PickTaskDto> GetPickTaskDetailAsync(Guid pickTaskId);
        Task AcceptPickTaskAsync(Guid pickTaskId, Guid staffId);
        Task UpdatePickTaskItemProgressAsync(Guid pickTaskId, Guid staffId, Guid productId, int pickedQty, string? imageUrl);
        Task CompletePickTaskAsync(Guid pickTaskId, Guid staffId);
        
        Task AcceptOrderAsync(Guid orderId, Guid staffId);
        Task ReportShortageAsync(Guid orderId, Guid staffId, ShortageAlertRequestDto alert);
        Task ConsolidateOrderAsync(Guid orderId, Guid staffId);
        Task HandoverOrderAsync(Guid orderId, Guid staffId, HandoverRequestDto dto);
        Task PostGoodsIssueAsync(Guid orderId, Guid staffId);

        // FUL-08: gộp pick nhiều đơn — cần Sales Manager duyệt trước (MultiPickApproval)
        Task<MultiPickApprovalDto> RequestMultiPickAsync(Guid staffId, List<Guid> orderIds);
        Task<MultiPickApprovalDto> DecideMultiPickAsync(Guid approvalId, Guid managerId, MultiPickDecisionRequestDto dto);
        Task<MultiPickExecuteResultDto> ExecuteMultiPickAsync(Guid staffId, List<Guid> orderIds);
    }
}
