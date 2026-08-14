namespace VietTien.API.DTOs.Warehouse
{
    public class WarehouseOrderListDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public DateTime ConfirmedAt { get; set; }
        public int TotalQuantity { get; set; }
        public decimal FinalPayment { get; set; }
        public string Status { get; set; } = string.Empty;
        public string AllocatedWarehouse { get; set; } = string.Empty;
        public string AllocatedWarehouseCode { get; set; } = string.Empty;
        public bool RequiresTransfer { get; set; }
        public int OrderProgress { get; set; }
        public DateTime? PickingStartedAt { get; set; }
        public DateTime? PickingCompletedAt { get; set; }
        public bool WarehouseConfirmed { get; set; }
        public bool SalesConfirmed { get; set; }
    }
}
