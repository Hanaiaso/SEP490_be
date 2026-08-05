using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;

namespace VietTien.API.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderPreviewDto> GetCheckoutSummaryAsync(Guid userId);
        Task<OrderResponseDto> PlaceOrderAsync(Guid userId, PlaceOrderRequestDto request);
        Task<DirectOrderResponseDto> PlaceDirectOrderAsync(PlaceDirectOrderRequestDto request);
        Task<SePayQrResponseDto> GenerateSePayQrAsync(Guid orderId, Guid callerUserId, string callerRole);
        Task ProcessSePayWebhookAsync(SePayWebhookDto payload, string providedToken);
        Task<PaymentStatusResponseDto> GetPaymentStatusAsync(Guid orderId, Guid callerUserId, string callerRole);
        Task<SalesDashboardStatsDto> GetSalesDashboardStatsAsync(Guid? scopedSalesStaffId = null);
        Task ConfirmDirectOrderPaymentAsync(Guid orderId);
        Task UploadInvoicePdfAsync(Guid orderId, string pdfBase64, Guid callerUserId, string callerRole);

        Task<PagedResultDto<OrderHistoryItemDto>> GetOrderHistoryAsync(
            Guid userId, OrderHistoryQueryDto query);
        Task<OrderHistoryDetailDto> GetOrderDetailForCustomerAsync(Guid userId, Guid orderId);
        Task<OrderHistoryDetailDto> TrackOrderPublicAsync(string search);
        Task RequestVatInvoiceAsync(Guid userId, Guid orderId);
        Task<SpendingStatsDto> GetSpendingStatsAsync(Guid userId, string period);

        Task<PagedResultDto<SalesOrderListDto>> GetSalesOrdersAsync(SalesOrderQueryDto query, Guid? salesStaffId = null);
        Task<SalesOrderDetailDto> GetSalesOrderDetailAsync(Guid orderId, Guid? salesStaffId = null);
        Task ConfirmOrderAsync(Guid orderId, Guid salesStaffId);
        Task RejectOrderAsync(Guid orderId, Guid salesStaffId, string reason);
        Task RequestCancelOrderAsync(Guid orderId, Guid customerId, RequestCancelOrderDto request);
        Task ProcessCancelRequestAsync(Guid orderId, Guid salesStaffId, ProcessCancelRequestDto request);

        // ─── LUỒNG 5: Giao hàng & Thu COD ─────────────────────────────────────
        Task<ScheduleDeliveryResponseDto> ScheduleDeliveryAsync(Guid scheduledByUserId, ScheduleDeliveryRequestDto dto);
        Task<List<DeliveryOrderListDto>> GetDeliveryOrdersAsync(Guid salesStaffId);
        Task<DeliveryResultResponseDto> RecordDeliveryResultAsync(Guid orderId, Guid salesStaffId, RecordDeliveryResultDto dto);
        
        Task CreateReturnExchangeRequestAsync(Guid orderId, Guid requestedByUserId, CreateReturnExchangeRequestDto dto);
        Task ProcessReturnExchangeRequestAsync(Guid requestId, Guid managerId, ProcessReturnExchangeRequestDto dto);

        // ─── BƯỚC 5: THU HỒI HÀNG (PICKUP) ────────────────────────────────────
        Task<List<PendingPickupDto>> GetPendingPickupsAsync(Guid userId);
        Task SchedulePickupAsync(Guid requestId, Guid userId, SchedulePickupRequestDto dto);
        Task ConfirmPickupAsync(Guid requestId, Guid userId);

        // ─── LUỒNG 5: Hủy đơn PAID (CR-06) ────────────────────────────────────
        Task RequestCancelPaidOrderAsync(Guid orderId, Guid requestedByUserId, string reason);
        Task<ReplacementOrderResponseDto> ApproveCancelAndCreateReplacementAsync(Guid originalOrderId, Guid managerId, CreateReplacementOrderDto dto);
        
        // ─── Đơn thay thế đổi trả (WF-15) ─────────────────────────────────────
        Task<ReplacementOrderResponseDto> CreateExchangeReplacementOrderAsync(Guid requestId, Guid userId);
    }
}
