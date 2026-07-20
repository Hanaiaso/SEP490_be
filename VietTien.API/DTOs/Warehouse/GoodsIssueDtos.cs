using System;
using System.Collections.Generic;

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
        public string Type { get; set; } = "ProductionMaterial"; // SalesOrder, StockTransfer, ProductionMaterial, Other
        public Guid? ReferenceId { get; set; } // e.g. ProductionRequestId (if exists)
        public Guid WarehouseId { get; set; }
        public string? Note { get; set; }
        public List<CreateGoodsIssueItemRequestDto> Items { get; set; } = new();
    }

    public class CreateGoodsIssueItemRequestDto
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public int Quantity { get; set; }
        public string? Note { get; set; }
    }
}
