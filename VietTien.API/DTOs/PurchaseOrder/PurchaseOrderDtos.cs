using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.PurchaseOrder
{
    public class CreatePurchaseOrderRequest
    {
        [Required]
        public Guid SupplierId { get; set; }
        [Required]
        public Guid WarehouseId { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string? Note { get; set; }
        public string? DeliveryTerms { get; set; }

        public List<CreatePurchaseOrderItemRequest> Items { get; set; } = new List<CreatePurchaseOrderItemRequest>();
    }

    public class CreatePurchaseOrderItemRequest
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int ExpectedQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string Unit { get; set; } = "Cái";
        public string? Note { get; set; }
    }

    public class PurchaseOrderDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public DateTime? IssuedAt { get; set; }
        public string? Note { get; set; }
        public string? DeliveryTerms { get; set; }

        public Guid CreatedById { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        
        public Guid SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;

        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;

        public List<PurchaseOrderItemDto> Items { get; set; } = new List<PurchaseOrderItemDto>();
    }

    public class PurchaseOrderItemDto
    {
        public Guid Id { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty; // Product.Name hoặc Material.Name
        public string ItemSku { get; set; } = string.Empty;  // Product.Sku hoặc rỗng
        public string ItemType { get; set; } = "Product";     // "Product" hoặc "Material"
        
        public int ExpectedQuantity { get; set; }
        public int ReceivedQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public class PurchaseOrderListDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int TotalExpectedQuantity { get; set; }
        public int TotalReceivedQuantity { get; set; }
    }

    public class DiscrepancyResolutionRequest
    {
        public string ResolutionType { get; set; } = string.Empty; // AcceptExcess, ReturnExcess, RequestSupplemental, CloseShort
        public string Reason { get; set; } = string.Empty;
    }
}
