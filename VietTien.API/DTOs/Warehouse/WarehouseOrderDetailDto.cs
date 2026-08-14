namespace VietTien.API.DTOs.Warehouse
{
    public class WarehouseOrderDetailDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string AllocatedWarehouse { get; set; } = string.Empty;
        public string AllocatedWarehouseCode { get; set; } = string.Empty;
        public int OrderProgress { get; set; }
        public DateTime? PickingStartedAt { get; set; }
        public DateTime? PickingCompletedAt { get; set; }
        public decimal FinalPayment { get; set; }

        public List<WarehouseOrderItemDto> Items { get; set; } = new List<WarehouseOrderItemDto>();
        public List<PickTaskDto> PickTasks { get; set; } = new List<PickTaskDto>();
    }

    public class PickTaskDto
    {
        public Guid PickTaskId { get; set; }
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal FinalPayment { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<WarehouseOrderItemDto> Items { get; set; } = new List<WarehouseOrderItemDto>();
    }

    public class WarehouseOrderItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string SKU { get; set; } = string.Empty;
        public int RequestedQuantity { get; set; }
        public int PhysicalStock { get; set; }
        public bool IsStockSufficient { get; set; }

        public int PackedQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public string? EvidenceImageUrl { get; set; }
        public int RequiredTransferQuantity { get; set; }
    }
}
