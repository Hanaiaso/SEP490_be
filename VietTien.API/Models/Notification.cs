using System;

namespace VietTien.API.Models
{
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        public Guid RecipientUserId { get; set; }
        public User? RecipientUser { get; set; }

        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        public Guid? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum NotificationType
    {
        SYS_01_CustomerAssigned,
        SYS_02_NewOrder,
        SYS_03_CodUnconfirmed25m,
        SYS_04_CodUnconfirmed30m,
        SYS_05_SePaySuccess,
        SYS_06_SePayError,
        SYS_07_WarehouseShortage,
        SYS_08_OrderReady,
        SYS_09_AllWarehousesReady,
        SYS_10_StockTransferDispatched,
        SYS_11_DeliveryScheduleConflict,
        SYS_12_DeliveryFailedThirdTime,
        SYS_13_CodUnderpaid,
        SYS_14_CustomerRequestChangeSales,
        SYS_15_ChangeSalesRequestResult,
        SYS_16_NewQuotationRequest,
        SYS_17_QuotationPendingApproval,
        SYS_18_POSentToWarehouse,
        SYS_19_GoodsReceiptDiscrepancy,
        SYS_20_LowStockAlert,
        SYS_21_QualityReturnRequested,
        SYS_22_AiMarketingPendingApproval,
        SYS_23_PaidOrderCancelledUnresolved,
        SYS_24_CustomerRequestedCancel,
        SYS_25_CancelRequestResult,
        SYS_26_WebhookRetryExhausted,
        SYS_27_QuotationNegotiationLimitReached,
        SYS_28_QuotationExpired,
        SYS_29_UpcomingDeliveryReminder,
        SYS_30_AccountStatusChanged,
        SYS_31_RoleChanged,
        SYS_32_PaymentAnomaly,
        SYS_33_ReviewReplyPosted,
        SYS_34_StockAdjustmentPendingApproval,
        SYS_35_StockAdjustmentDecisionResult,
        SYS_36_OrderUnblockedForRedelivery,
        SYS_37_DebtSettled,
        SYS_38_SessionRevoked,

        // Nhóm C: DeliveryTrip (luồng Trip-based) — ngưỡng escalate@3 riêng biệt với SYS_12 (block@3 của luồng theo Order cũ)
        SYS_39_DeliveryTripAttemptEscalation,

        // Nhóm C: FUL-08 — đề xuất gộp pick nhiều đơn chờ Sales Manager duyệt
        SYS_40_MultiPickRequestPendingApproval,

        // DEF-L4-003: đóng phiên kiểm kê (InventoryCountingSession bên main)
        SYS_41_InventoryCountSessionClosed,

        // Báo giá ≥ ngưỡng B2B: Sale không còn tự nhận xử lý được, Sales Manager phải phân công thủ
        // công cho người có kinh nghiệm phù hợp.
        SYS_42_QuotationNeedsManagerAssignment,
        SYS_43_QuotationAssignedByManager,

        // Luồng cập nhật giá hàng hóa (ProductPriceUpdateOrder): CEO đề xuất -> Sales Manager phân
        // công + thông báo khách hàng -> Sales Staff thực hiện đúng ngày hiệu lực.
        SYS_44_ProductPriceUpdateOrderProposed,
        SYS_45_ProductPriceUpdateOrderAssigned,
        SYS_46_ProductPriceUpdateScheduleNotice,
        SYS_47_ProductPriceUpdateOrderExecuted,
        SYS_48_ProductPriceUpdateOrderCancelled,

        // Sale đã nhập số hóa đơn đỏ thật (lấy từ bên thứ 3) cho đơn hàng khách đã yêu cầu xuất hóa đơn.
        SYS_49_RedInvoiceIssued,

        // Sản phẩm/nguyên vật liệu vượt ngưỡng tồn đọng (ExcessThreshold/MaxStockThreshold) — mirror SYS_20.
        SYS_50_ExcessStockAlert,

        // Mặt hàng không có giao dịch xuất kho trong X ngày (chậm luân chuyển) — SlowMovingStockAlertJob.
        SYS_51_SlowMovingStockAlert
    }
}
