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
        SYS_25_CancelRequestResult
    }
}
