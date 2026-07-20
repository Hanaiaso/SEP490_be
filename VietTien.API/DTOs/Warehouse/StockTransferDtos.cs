using System;
using System.Collections.Generic;
using VietTien.API.Models;

namespace VietTien.API.DTOs.Warehouse
{
    public class StockTransferDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public Guid SourceWarehouseId { get; set; }
        public string SourceWarehouseName { get; set; } = string.Empty;
        public Guid DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public string CreatedByUserName { get; set; } = string.Empty;
        public StockTransferStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public string? Note { get; set; }
        public string? ReceiveNote { get; set; }
        public string? ProofImageUrl { get; set; }
        public string? NotificationEmail { get; set; }
        public List<StockTransferItemDto> Items { get; set; } = new();
    }

    public class StockTransferItemDto
    {
        public Guid Id { get; set; }
        public Guid StockTransferId { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; set; } = "Product";
        public int Quantity { get; set; }
        public int? ReceivedQuantity { get; set; }
    }

    public class CreateStockTransferDto
    {
        public Guid SourceWarehouseId { get; set; }
        public Guid DestinationWarehouseId { get; set; }
        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }
        public string? Note { get; set; }
        
        // Gửi email thông báo cho nhân viên
        public string? NotificationEmail { get; set; }
        public Guid? AssignedStaffId { get; set; } // Nhân viên kho được chọn

        public List<CreateStockTransferItemDto> Items { get; set; } = new();
    }

    public class CreateStockTransferItemDto
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateStockTransferDto
    {
        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }
        public string? Note { get; set; }
        public List<CreateStockTransferItemDto> Items { get; set; } = new();
    }

    public class ReceiveStockTransferDto
    {
        // Nhận dữ liệu là chuỗi JSON: '[{"productId": "...", "receivedQuantity": 10}]'
        public string ItemsJson { get; set; } = string.Empty;
        public string? Note { get; set; }
        public List<Microsoft.AspNetCore.Http.IFormFile>? ProofImages { get; set; }
    }

    public class ReceiveStockTransferItemDto
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public int ReceivedQuantity { get; set; }
    }
}
