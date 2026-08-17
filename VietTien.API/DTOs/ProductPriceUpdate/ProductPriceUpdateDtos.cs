using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.ProductPriceUpdate
{
    public class CreateProductPriceUpdateOrderRequest
    {
        [Required]
        public DateTime ScheduledEffectiveDate { get; set; }

        [MaxLength(1000)]
        public string? ProposalNote { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Đợt cập nhật giá phải có ít nhất 1 sản phẩm.")]
        public List<ProductPriceUpdateOrderItemRequest> Items { get; set; } = new List<ProductPriceUpdateOrderItemRequest>();
    }

    public class ProductPriceUpdateOrderItemRequest
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Giá mới phải lớn hơn 0")]
        public decimal NewPrice { get; set; }
    }

    // Sales Manager phân công 1 Sales Staff cụ thể phụ trách thực hiện đợt cập nhật giá (đồng thời
    // là hành động gửi thông báo cho khách hàng bị ảnh hưởng) — mirror AssignQuotationRequest.
    public class AssignPriceUpdateOrderRequest
    {
        [Required(ErrorMessage = "Vui lòng chọn nhân viên Sale để phân công.")]
        public Guid StaffId { get; set; }
    }

    public class CancelPriceUpdateOrderRequest
    {
        [MaxLength(1000)]
        public string? Reason { get; set; }
    }

    public class ProductPriceUpdateOrderDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;

        // Trạng thái đúng/trễ hạn suy diễn — CEO dùng để theo dõi tuân thủ, không lưu DB.
        public string ComplianceStatus { get; set; } = string.Empty;

        public Guid ProposedByUserId { get; set; }
        public string ProposedByName { get; set; } = string.Empty;
        public DateTime ProposedAt { get; set; }
        public string? ProposalNote { get; set; }
        public DateTime ScheduledEffectiveDate { get; set; }

        public Guid? AssignedByManagerId { get; set; }
        public string? AssignedByManagerName { get; set; }
        public Guid? AssignedSalesStaffId { get; set; }
        public string? AssignedSalesStaffName { get; set; }
        public DateTime? NotifiedAt { get; set; }

        public Guid? ExecutedByUserId { get; set; }
        public string? ExecutedByName { get; set; }
        public DateTime? ExecutedAt { get; set; }

        public Guid? CancelledByUserId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }

        public List<ProductPriceUpdateOrderItemDto> Items { get; set; } = new List<ProductPriceUpdateOrderItemDto>();
    }

    public class ProductPriceUpdateOrderItemDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImageUrl { get; set; }
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
    }
}
