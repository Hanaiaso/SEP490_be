namespace VietTien.API.DTOs.Admin
{
    // ─── KPI (business.md §5) ────────────────────────────────────────────────

    public class KpiSnapshotDto
    {
        public Guid? SalesStaffId { get; set; } // null = toàn hệ thống/toàn đội
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }

        public decimal Revenue { get; set; }
        public int CompletedOrderCount { get; set; }

        public double DeliverySuccessRate { get; set; } // 0..1
        public int DeliveryAttemptedOrderCount { get; set; }

        public double? ProcessingSpeedAvgHours { get; set; } // null nếu không có đơn nào có ConfirmedAt trong kỳ

        public double ReturningCustomerRate { get; set; } // 0..1
        public int CustomersInScopeCount { get; set; }
    }

    public class SalesStaffKpiDto
    {
        public Guid SalesStaffId { get; set; }
        public string SalesStaffName { get; set; } = string.Empty;
        public KpiSnapshotDto Kpi { get; set; } = new();
    }

    // ─── Sales Staff (cá nhân) ────────────────────────────────────────────────

    public class SalesStaffDashboardDto
    {
        public KpiSnapshotDto Kpi { get; set; } = new();
    }

    // ─── Sales Manager (toàn đội) ─────────────────────────────────────────────

    public class SalesManagerDashboardDto
    {
        public KpiSnapshotDto TeamKpi { get; set; } = new();
        public List<SalesStaffKpiDto> StaffBreakdown { get; set; } = new();
        public List<PaymentExceptionDto> OpenExceptions { get; set; } = new();
        public List<CustomerDebtDto> OverdueDebts { get; set; } = new();
        public int CodSlaBreachCountToday { get; set; } // đơn COD bị hủy do quá 35' trong hôm nay (nguồn: JobRun/OrderSlaJob)
    }

    public class PaymentExceptionDto
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CustomerDebtDto
    {
        public Guid Id { get; set; }
        public Guid CustomerProfileId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal DebtAmount { get; set; }
        public int OverdueDays { get; set; }
    }

    // ─── CEO (tổng quan) ──────────────────────────────────────────────────────

    public class CeoDashboardDto
    {
        public KpiSnapshotDto OrgKpi { get; set; } = new();
        public InventorySummaryDto Inventory { get; set; } = new();
        public PurchaseOrderSummaryDto PurchaseOrders { get; set; } = new();
        public DiscrepancySummaryDto Discrepancy { get; set; } = new();
    }

    public class InventorySummaryDto
    {
        public int TotalSkus { get; set; }
        public int LowStockCount { get; set; } // ReorderThreshold đã cấu hình và AvailableQuantity dưới ngưỡng (Phase 2)
        public decimal EstimatedInventoryValue { get; set; } // Σ OnHandQuantity * StandardListedPrice (chỉ hàng thành phẩm có giá niêm yết)
    }

    public class PurchaseOrderSummaryDto
    {
        public Dictionary<string, int> CountsByStatus { get; set; } = new();
        public List<PurchaseOrderSummaryItemDto> RecentOpenPurchaseOrders { get; set; } = new();
    }

    public class PurchaseOrderSummaryItemDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
    }

    public class DiscrepancySummaryDto
    {
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public int GoodsReceiptCount { get; set; }
        public int TotalShortQuantity { get; set; }
        public int TotalExcessQuantity { get; set; }
        public int TotalDamagedQuantity { get; set; }
        public int TotalWrongItemQuantity { get; set; }
    }

    // ─── Admin (tổng quan hệ thống) ────────────────────────────────────────────

    public class AdminDashboardDto
    {
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }

        public decimal Revenue { get; set; }
        public decimal PreviousPeriodRevenue { get; set; }

        // Đếm theo trạng thái hiện tại (live queue), không phụ thuộc kỳ chọn
        public int ReceivedOrderCount { get; set; } // OrderStatus.Confirmed hoặc Processing
        public int ShippingOrderCount { get; set; } // DeliveryStatus.InDelivery

        // Đếm trong kỳ đã chọn
        public int DeliveredOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }
        public int PreviousPeriodCancelledOrderCount { get; set; }

        public int PendingQuotationApprovalCount { get; set; } // Quotation đang chờ Manager/CEO duyệt
        public int StaleOrderCount { get; set; } // Đơn đã nhận nhưng quá hạn xử lý (>48h)
        public int LowStockAlertCount { get; set; }

        public List<AdminLowStockItemDto> LowStockItems { get; set; } = new();
        public List<AdminTopProductDto> TopProducts { get; set; } = new();
        public List<AdminTopCustomerDto> TopCustomers { get; set; } = new();
    }

    public class AdminLowStockItemDto
    {
        public string Name { get; set; } = string.Empty;
        public int AvailableQuantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public bool Urgent { get; set; }
    }

    public class AdminTopProductDto
    {
        public string Name { get; set; } = string.Empty;
        public int OrderCount { get; set; }
    }

    public class AdminTopCustomerDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal TotalSpent { get; set; }
    }
}
