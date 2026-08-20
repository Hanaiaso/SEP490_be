using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        public int? DeliveryVehicleId { get; set; }
        public string? DeliveryShift { get; set; }
        public DateTime? ScheduledDeliveryDate { get; set; }
        public decimal TotalWeightKg { get; set; }
        public List<StockTransferItemDto> Items { get; set; } = new();

        // Chỉ có giá trị ngay tại thời điểm DispatchAsync vừa tạo phiếu xuất kho (mục 12) — không nạp lại ở các lần GET sau.
        public Guid? GoodsIssueId { get; set; }
        public string? GoodsIssueCode { get; set; }
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
        public decimal? WeightKg { get; set; }
    }

    public class CreateStockTransferDto : IValidatableObject
    {
        [Required]
        public Guid SourceWarehouseId { get; set; }

        [Required]
        public Guid DestinationWarehouseId { get; set; }

        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }

        // Gửi email thông báo cho nhân viên
        [EmailAddress(ErrorMessage = "Email thông báo không hợp lệ.")]
        public string? NotificationEmail { get; set; }
        public Guid? AssignedStaffId { get; set; } // Nhân viên kho được chọn

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu chuyển kho phải có ít nhất 1 mặt hàng.")]
        public List<CreateStockTransferItemDto> Items { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (SourceWarehouseId != Guid.Empty && SourceWarehouseId == DestinationWarehouseId)
            {
                yield return new ValidationResult(
                    "Kho nguồn và kho đích không được trùng nhau.",
                    new[] { nameof(DestinationWarehouseId) });
            }

            if (ExpectedDispatchDate.HasValue && ExpectedReceiveDate.HasValue
                && ExpectedReceiveDate.Value < ExpectedDispatchDate.Value)
            {
                yield return new ValidationResult(
                    "Ngày nhận dự kiến phải sau ngày xuất dự kiến.",
                    new[] { nameof(ExpectedReceiveDate) });
            }
        }
    }

    public class CreateStockTransferItemDto
    {
        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }
    }

    public class UpdateStockTransferDto : IValidatableObject
    {
        public DateTime? ExpectedDispatchDate { get; set; }
        public DateTime? ExpectedReceiveDate { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu chuyển kho phải có ít nhất 1 mặt hàng.")]
        public List<CreateStockTransferItemDto> Items { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ExpectedDispatchDate.HasValue && ExpectedReceiveDate.HasValue
                && ExpectedReceiveDate.Value < ExpectedDispatchDate.Value)
            {
                yield return new ValidationResult(
                    "Ngày nhận dự kiến phải sau ngày xuất dự kiến.",
                    new[] { nameof(ExpectedReceiveDate) });
            }
        }
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
