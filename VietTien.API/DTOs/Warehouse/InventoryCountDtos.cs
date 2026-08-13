using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    // INV-01: kiểm kê kho 2 bước (snapshot lý thuyết -> ghi số đếm thực tế)
    public class CreateInventoryCountSessionRequestDto
    {
        [Required]
        public Guid WarehouseId { get; set; }
    }

    public class InventoryCountSessionDto
    {
        public Guid Id { get; set; }
        public Guid WarehouseId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? TheoreticalLockedAt { get; set; }
        public List<InventoryCountLineDto> Lines { get; set; } = new();
    }

    public class InventoryCountLineDto
    {
        public Guid Id { get; set; }
        public Guid InventoryId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int TheoreticalQuantity { get; set; }
        public int? ActualQuantity { get; set; }
        public DateTime? CountedAt { get; set; }
    }

    public class RecordCountLineRequestDto
    {
        [Required]
        public Guid InventoryId { get; set; }

        // Không dùng [Range] ở đây: âm phải trả cùng mã lỗi COUNT_LINE_INVALID qua service,
        // giống cách COD_AMOUNT_INVALID được xử lý ở luồng DeliveryTrip.
        public int ActualQuantity { get; set; }
    }
}
