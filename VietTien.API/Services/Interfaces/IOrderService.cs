using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.SePay;

namespace VietTien.API.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderPreviewDto> GetCheckoutSummaryAsync(Guid userId, List<Guid>? cartItemIds = null);
        Task<OrderResponseDto> PlaceOrderAsync(Guid userId, PlaceOrderRequestDto request);
        Task<DirectOrderResponseDto> PlaceDirectOrderAsync(PlaceDirectOrderRequestDto request, Guid staffId);
        Task<SePayQrResponseDto> GenerateSePayQrAsync(Guid orderId, Guid callerUserId, string callerRole);
        Task ProcessSePayWebhookAsync(SePayWebhookDto payload, string providedToken);
        Task<PaymentStatusResponseDto> GetPaymentStatusAsync(Guid orderId, Guid callerUserId, string callerRole);
        Task<SalesDashboardStatsDto> GetSalesDashboardStatsAsync(Guid? scopedSalesStaffId = null);
        Task<List<DashboardOrderDto>> GetSalesDashboardDrillDownAsync(string metric, Guid? scopedSalesStaffId = null);
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

        // UC-34: Sales Manager xử lý xung đột lịch xe/ca
        Task<List<DeliveryScheduleConflictDto>> GetPendingDeliveryConflictsAsync();
        Task<DeliveryScheduleConflictDto> ResolveDeliveryConflictAsync(Guid conflictId, Guid managerId, ResolveDeliveryConflictRequestDto dto);
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
        Task<PaymentReallocationResponseDto> CreatePaymentReallocationAsync(Guid callerUserId, CreatePaymentReallocationRequestDto request);

        // ─── P2-6: Sales Manager xử lý đơn bị khóa & công nợ COD (UC-35) ──────
        Task<List<BlockedOrderDto>> GetBlockedOrdersAsync();
        Task UnblockOrderForRedeliveryAsync(Guid orderId, Guid managerId, string reason);
        Task<List<CustomerDebtManagementDto>> GetDebtsAsync(string? status = null);
        Task SettleDebtAsync(Guid debtId, Guid managerId, string? note);

        // ─── Đơn thay thế đổi trả (WF-15) ─────────────────────────────────────
        Task<ReplacementOrderResponseDto> CreateExchangeReplacementOrderAsync(Guid requestId, Guid userId);
    }
}
