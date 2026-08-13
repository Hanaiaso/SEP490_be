using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class OpenInventoryCountSessionRequest
    {
        [Required]
        public Guid WarehouseId { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class RecordCountItemRequest
    {
        [Range(0, int.MaxValue, ErrorMessage = "Số đếm thực tế phải lớn hơn hoặc bằng 0")]
        public int PhysicalQuantity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class InventoryCountSessionItemDto
    {
        public Guid Id { get; set; }
        public Guid InventoryId { get; set; }

        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;

        public int SystemQuantity { get; set; }
        public int? PhysicalQuantity { get; set; }
        public int? Variance { get; set; }
        public string? Note { get; set; }

        public bool AutoApplied { get; set; }
        public Guid? StockAdjustmentId { get; set; }
    }

    public class InventoryCountSessionDto
    {
        public Guid Id { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }

        public Guid OpenedByUserId { get; set; }
        public string OpenedByName { get; set; } = string.Empty;
        public DateTime OpenedAt { get; set; }

        public Guid? ClosedByUserId { get; set; }
        public string? ClosedByName { get; set; }
        public DateTime? ClosedAt { get; set; }

        public List<InventoryCountSessionItemDto> Items { get; set; } = new();
    }

    // Tóm tắt trả về sau khi đóng phiên: bao nhiêu dòng áp dụng thẳng, bao nhiêu dòng chờ CEO duyệt.
    public class CloseInventoryCountSessionResultDto
    {
        public InventoryCountSessionDto Session { get; set; } = null!;
        public int AutoAppliedCount { get; set; }
        public int PendingApprovalCount { get; set; }
    }
}
