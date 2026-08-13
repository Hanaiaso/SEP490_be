using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Material
{
    /// <summary>
    /// L3-INV-05: convenience endpoint gộp 3 bước Create -> UploadProof -> Post của quy trình
    /// GoodsIssue (Type=ProductionMaterial, xem GoodsIssueController/GoodsIssueService) thành 1 lần
    /// gọi — nghiệp vụ và validate bằng chứng thật nằm nguyên ở GoodsIssueService, DTO này chỉ là
    /// hình dạng request cho endpoint rút gọn.
    /// </summary>
    public class CreateProductionIssueRequestDto
    {
        [Required]
        public Guid WarehouseId { get; set; }

        [Required(ErrorMessage = "Tên người nhận là bắt buộc.")]
        [MaxLength(200)]
        public string ExternalRecipientName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bộ phận sản xuất nhận là bắt buộc.")]
        [MaxLength(200)]
        public string Department { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thời điểm nhận nguyên liệu là bắt buộc.")]
        public DateTime ReceivedAt { get; set; }

        [Required(ErrorMessage = "Số biên bản/chứng từ giấy là bắt buộc.")]
        [MaxLength(100)]
        public string PaperDocumentNumber { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? UsagePurpose { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu xuất phải có ít nhất 1 mặt hàng.")]
        public List<ProductionIssueItemDto> Items { get; set; } = new();
    }

    /// <summary>
    /// Multipart form field naming để gửi list: Items[0].ProductId, Items[0].Quantity, Items[1]...
    /// </summary>
    public class ProductionIssueItemDto
    {
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}
