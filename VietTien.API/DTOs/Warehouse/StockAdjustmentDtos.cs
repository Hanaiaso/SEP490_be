using System;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class CreateStockAdjustmentRequest
    {
        [Required]
        public Guid InventoryId { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng đếm thực tế phải lớn hơn hoặc bằng 0")]
        public int PhysicalQuantity { get; set; }

        [Required(ErrorMessage = "Lý do chênh lệch là bắt buộc.")]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }

    public class StockAdjustmentDecisionRequest
    {
        [Required(ErrorMessage = "Quyết định là bắt buộc.")]
        [RegularExpression("^(Approved|Rejected)$",
            ErrorMessage = "Quyết định không hợp lệ. Chọn: Approved / Rejected.")]
        public string Decision { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class StockAdjustmentDto
    {
        public Guid Id { get; set; }
        public Guid InventoryId { get; set; }

        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;

        public int SystemQuantity { get; set; }
        public int PhysicalQuantity { get; set; }
        public int Variance { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public Guid ProposedByUserId { get; set; }
        public string ProposedByName { get; set; } = string.Empty;
        public DateTime ProposedAt { get; set; }

        public Guid? DecidedByUserId { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
    }
}
