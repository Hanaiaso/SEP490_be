using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public class Inventory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Chỉ 1 trong 2 được fill: ProductId (tồn kho thành phẩm) hoặc MaterialId (tồn kho nguyên liệu)
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        public Guid WarehouseLocationId { get; set; }

        [ConcurrencyCheck]
        public int OnHandQuantity { get; set; }

        public int ReservedQuantity { get; set; }
        public int AllocatedQuantity { get; set; }
        public int InTransitQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public int QuarantineQuantity { get; set; }

        public int AvailableQuantity => Math.Max(0, OnHandQuantity - ReservedQuantity - AllocatedQuantity - DamagedQuantity - QuarantineQuantity);

        // Ngưỡng cảnh báo tồn thấp cho hàng thành phẩm (null = chưa cấu hình, không cảnh báo dòng này)
        public int? ReorderThreshold { get; set; }

        public Guid? LastUpdatedByUserId { get; set; }
        public DateTime? LastUpdatedAt { get; set; }

        // Navigation Property
        public Product? Product { get; set; }
        public Material? Material { get; set; }
        public WarehouseLocation WarehouseLocation { get; set; } = null!;
        public User? LastUpdatedByUser { get; set; }
    }
}
