namespace VietTien.API.DTOs.Warehouse
{
    public class WarehouseDashboardDto
    {
        public DateTime GeneratedAt { get; set; }
        public List<WarehouseSummaryDto> Warehouses { get; set; } = new();
        public WarehouseOutboundKpiDto Outbound { get; set; } = new();
        public WarehouseInboundKpiDto Inbound { get; set; } = new();
        public WarehouseInventoryKpiDto InventoryOps { get; set; } = new();
        public List<WarehouseDailyVolumeDto> WeeklyVolume { get; set; } = new();
        public List<StockHealthItemDto> StockHealth { get; set; } = new();
        public List<RecentPickTaskDto> RecentPickTasks { get; set; } = new();
        public List<PendingPurchaseOrderDto> PendingPurchaseOrders { get; set; } = new();
        public List<LowStockItemDto> LowStockAlerts { get; set; } = new();
        public List<InTransitTransferDto> InTransitTransfers { get; set; } = new();
        public List<RecentMaterialIssueDto> RecentMaterialIssues { get; set; } = new();
    }

    public class WarehouseSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class WarehouseOutboundKpiDto
    {
        public int PendingOrders { get; set; }
        public int PickingInProgress { get; set; }
        public int ConsolidationArea { get; set; }
        public int CompletedToday { get; set; }
    }

    public class WarehouseInboundKpiDto
    {
        public int PendingPurchaseOrders { get; set; }
        public int ReceiptsInProgress { get; set; }
        public int QualityCheckPending { get; set; }
        public int ReturnQuarantinePending { get; set; }
    }

    public class WarehouseInventoryKpiDto
    {
        public int LowStockCount { get; set; }
        public int SlowMovingCount { get; set; }
        public int TransfersInTransit { get; set; }
        public int ActiveWarehouses { get; set; }
    }

    public class WarehouseDailyVolumeDto
    {
        public DateTime Date { get; set; }
        public int Outbound { get; set; }
        public int Inbound { get; set; }
    }

    public class StockHealthItemDto
    {
        public string Name { get; set; } = string.Empty;
        public int OnHand { get; set; }
        public int Threshold { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class RecentPickTaskDto
    {
        public Guid Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class PendingPurchaseOrderDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ProgressPercent { get; set; }
    }

    public class LowStockItemDto
    {
        public string Name { get; set; } = string.Empty;
        public int OnHand { get; set; }
        public int Threshold { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class InTransitTransferDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string SourceWarehouse { get; set; } = string.Empty;
        public string DestinationWarehouse { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class RecentMaterialIssueDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
