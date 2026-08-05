using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class InventoryItemDto
    {
        public Guid Id { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;
        public string ItemType { get; set; } = "Product";

        // Aliases cho tương thích Frontend
        public string ProductName => ItemName;
        public string ProductSku => ItemSku;
        
        public int OnHandQuantity { get; set; }
        public int ReservedQuantity { get; set; }
        public int AvailableQuantity { get; set; }

        public Guid? LastUpdatedByUserId { get; set; }
        public string? LastUpdatedByUserName { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
    }

    public class AdjustInventoryRequest
    {
        [Range(0, int.MaxValue, ErrorMessage = "Số lượng mới phải lớn hơn hoặc bằng 0")]
        public int NewQuantity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class AddInventoryRequest
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        [Required]
        public Guid WarehouseLocationId { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng khởi tạo phải lớn hơn hoặc bằng 0")]
        public int InitialQuantity { get; set; }
    }

    public class PaginatedList<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
