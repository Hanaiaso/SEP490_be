namespace VietTien.API.Models
{
    public class PurchaseOrderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PurchaseOrderId { get; set; }

        // Chỉ 1 trong 2 được fill: ProductId (mua thành phẩm/hàng hóa) hoặc MaterialId (mua nguyên liệu)
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        public int ExpectedQuantity { get; set; }
        public int ReceivedQuantity { get; set; } // Updated by Warehouse
        
        public decimal UnitPrice { get; set; }
        public string Unit { get; set; } = "Cái"; // Đơn vị tính
        
        public string? Note { get; set; }

        // Navigation Properties
        public PurchaseOrder PurchaseOrder { get; set; } = null!;
        public Product? Product { get; set; }
        public Material? Material { get; set; }

        // Helper: Lấy tên hiển thị
        public string DisplayName => Product?.Name ?? Material?.Name ?? "N/A";
        public string DisplayUnit => Product?.Unit ?? Material?.Unit ?? Unit;
    }
}
