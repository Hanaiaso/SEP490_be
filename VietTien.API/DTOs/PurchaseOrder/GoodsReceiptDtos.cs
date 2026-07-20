namespace VietTien.API.DTOs.PurchaseOrder
{
    public class CreateGoodsReceiptRequest
    {
        public string? Note { get; set; }
        public List<CreateGoodsReceiptItemRequest> Items { get; set; } = new List<CreateGoodsReceiptItemRequest>();
    }

    public class CreateGoodsReceiptItemRequest
    {
        public Guid PurchaseOrderItemId { get; set; }
        public int AcceptedQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public int ExcessQuantity { get; set; }
        public int ShortQuantity { get; set; }
        public int WrongItemQuantity { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Note { get; set; }
    }

    public class GoodsReceiptDto
    {
        public Guid Id { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid ReceivedByUserId { get; set; }
        public string ReceivedByUserName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string? Note { get; set; }

        public List<GoodsReceiptItemDto> Items { get; set; } = new List<GoodsReceiptItemDto>();
    }

    public class GoodsReceiptItemDto
    {
        public Guid Id { get; set; }
        public Guid PurchaseOrderItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;  // Product.Name hoặc Material.Name
        public string ItemSku { get; set; } = string.Empty;   // Product.Sku hoặc rỗng
        public string ItemType { get; set; } = "Product";     // "Product" hoặc "Material"
        
        public int AcceptedQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public int ExcessQuantity { get; set; }
        public int ShortQuantity { get; set; }
        public int WrongItemQuantity { get; set; }
        
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Note { get; set; }
    }
}
