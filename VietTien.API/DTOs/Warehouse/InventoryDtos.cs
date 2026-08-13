using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    // L3-INV-06: cùng phép so sánh ngưỡng mà LowStockAlertJob dùng để bắn notification định kỳ,
    // nhưng phơi ra dạng đọc trực tiếp (không side-effect, không cooldown) cho GET /api/inventory/low-stock-alerts.
    public class LowStockAlertDto
    {
        public string ItemType { get; set; } = string.Empty; // "Product" | "Material"
        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string? ItemSku { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public double AvailableQuantity { get; set; }
        public double Threshold { get; set; }
        public string SuggestedAction { get; set; } = string.Empty;
    }

    public class InventoryItemDto
    {
        public Guid Id { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;
        public string ItemType { get; set; } = "Product";
        public string Unit { get; set; } = string.Empty;

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

    public class InventoryReportDto
    {
        public int TotalSkus { get; set; }
        public decimal TotalInventoryValue { get; set; }
        public int TotalWarehouses { get; set; }
        public int LowStockCount { get; set; }
        public List<CategoryStockDto> CategoryBreakdown { get; set; } = new List<CategoryStockDto>();
        public List<StockMovementPointDto> StockMovement { get; set; } = new List<StockMovementPointDto>();
        public List<InventoryItemDto> TopLowStockItems { get; set; } = new List<InventoryItemDto>();
    }

    public class CategoryStockDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int TotalOnHand { get; set; }
        public decimal TotalValue { get; set; }
    }

    public class StockMovementPointDto
    {
        public DateTime Date { get; set; }
        public int TotalIn { get; set; }
        public int TotalOut { get; set; }
    }

    public class SlowMovingItemDto
    {
        public Guid Id { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemType { get; set; } = "Product";
        public string Sku { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int OnHandQuantity { get; set; }
        public DateTime? LastOutboundAt { get; set; }

        // null = chưa từng có giao dịch xuất kho nào được ghi nhận
        public int? DaysSinceLastOutbound { get; set; }
        public string Suggestion { get; set; } = string.Empty;
    }

    public class ShiftInventoryCountRequestDto
    {
        [Required]
        public Guid WarehouseId { get; set; }
        public Guid? ShiftId { get; set; }

        [Required]
        public DateTime CountDate { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách kiểm kê không được để trống.")]
        public List<ShiftInventoryCountItemDto> Items { get; set; } = new List<ShiftInventoryCountItemDto>();
    }

    public class ShiftInventoryCountItemDto
    {
        [Required]
        public Guid InventoryId { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng đếm thực tế phải lớn hơn hoặc bằng 0")]
        public int ActualQuantity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class ShiftInventoryCountResultDto
    {
        public int TotalCounted { get; set; }
        public int AdjustedCount { get; set; }
        public List<ShiftAdjustedItemDto> AdjustedItems { get; set; } = new List<ShiftAdjustedItemDto>();
    }

    public class ShiftAdjustedItemDto
    {
        public Guid InventoryId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int OldQuantity { get; set; }
        public int NewQuantity { get; set; }
    }
}
