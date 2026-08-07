using System;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum TransactionType
    {
        GoodsReceipt,
        GoodsIssue,
        StockAdjustment,
        QuarantineToAvailable,
        ReturnToSupplier
    }

    public class StockTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InventoryId { get; set; }

        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        public Guid WarehouseLocationId { get; set; }

        public int QuantityChange { get; set; } // Positive for addition, Negative for subtraction
        public TransactionType TransactionType { get; set; }

        public Guid? ReferenceId { get; set; } // E.g., GoodsReceiptId or GoodsIssueId

        [MaxLength(500)]
        public string? Note { get; set; }

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Inventory Inventory { get; set; } = null!;
        public Product? Product { get; set; }
        public Material? Material { get; set; }
        public WarehouseLocation WarehouseLocation { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
    }
}
