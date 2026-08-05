using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    public class GoodsIssueDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Guid? ReferenceId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public Guid IssuedByUserId { get; set; }
        public string IssuedByName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? ImageProofUrl { get; set; }
        
        // Mở rộng WF-17
        public string? ExternalRecipientName { get; set; }
        public string? Department { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public string? PaperDocumentNumber { get; set; }
        public string? UsagePurpose { get; set; }

        public Guid? ReversalForIssueId { get; set; }
        public string? ReversalReason { get; set; }
        public bool IsReversal { get; set; }

        public string? Note { get; set; }
        public List<GoodsIssueItemDto> Items { get; set; } = new();
    }

    public class GoodsIssueItemDto
    {
        public Guid Id { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;
        public string ItemType { get; set; } = "Product";
        public string Unit { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? Note { get; set; }
    }

    public class CreateGoodsIssueRequestDto
    {
        [Required]
        [RegularExpression("^(SalesOrder|StockTransfer|ProductionMaterial|Other)$",
            ErrorMessage = "Loại phiếu xuất không hợp lệ. Chọn: SalesOrder / StockTransfer / ProductionMaterial / Other.")]
        public string Type { get; set; } = "ProductionMaterial"; // SalesOrder, StockTransfer, ProductionMaterial, Other

        public Guid? ReferenceId { get; set; }

        [Required]
        public Guid WarehouseId { get; set; }

        // Mở rộng WF-17
        [MaxLength(200)]
        public string? ExternalRecipientName { get; set; }

        [MaxLength(200)]
        public string? Department { get; set; }

        public DateTime? ReceivedAt { get; set; }

        [MaxLength(100)]
        public string? PaperDocumentNumber { get; set; }

        [MaxLength(1000)]
        public string? UsagePurpose { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu xuất kho phải có ít nhất 1 mặt hàng.")]
        public List<CreateGoodsIssueItemRequestDto> Items { get; set; } = new();
    }

    public class CreateGoodsIssueItemRequestDto
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class UpdateGoodsIssueHandoverDto
    {
        [Required(ErrorMessage = "Tên người nhận là bắt buộc.")]
        [MaxLength(200)]
        public string ExternalRecipientName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phòng ban là bắt buộc.")]
        [MaxLength(200)]
        public string Department { get; set; } = string.Empty;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "Số chứng từ giấy là bắt buộc.")]
        [MaxLength(100)]
        public string PaperDocumentNumber { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? UsagePurpose { get; set; }
    }

    public class CreateReversalRequestDto
    {
        [Required(ErrorMessage = "Lý do đảo bút toán là bắt buộc.")]
        [MaxLength(1000)]
        public string ReversalReason { get; set; } = string.Empty;
    }
}
