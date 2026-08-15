using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.PurchaseOrder
{
    public class CreateGoodsReceiptRequest
    {
        [MaxLength(1000)]
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu nhập kho phải có ít nhất 1 mặt hàng.")]
        public List<CreateGoodsReceiptItemRequest> Items { get; set; } = new List<CreateGoodsReceiptItemRequest>();
    }

    public class CreateGoodsReceiptItemRequest
    {
        [Required]
        public Guid PurchaseOrderItemId { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng chấp nhận phải lớn hơn hoặc bằng 0")]
        public int AcceptedQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng hư hỏng phải lớn hơn hoặc bằng 0")]
        public int DamagedQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng dư phải lớn hơn hoặc bằng 0")]
        public int ExcessQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng thiếu phải lớn hơn hoặc bằng 0")]
        public int ShortQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng sai hàng phải lớn hơn hoặc bằng 0")]
        public int WrongItemQuantity { get; set; }

        [MaxLength(100)]
        public string? BatchNumber { get; set; }

        public DateTime? ExpiryDate { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    // Sửa số lượng của phiếu nhập CÒN Ở TRẠNG THÁI DRAFT (trước khi Post). Chỉ nhận 3 số liệu nhân
    // viên thực sự đếm được — Đạt/Hỏng/Sai loại; Thừa/Thiếu tự tính lại ở server theo đúng công thức
    // đã dùng khi tạo phiếu (CreateFromPOAsync), tránh client tính sai lệch dẫn tới bị từ chối oan.
    public class UpdateGoodsReceiptRequest
    {
        [Required]
        [MinLength(1, ErrorMessage = "Phiếu nhập kho phải có ít nhất 1 mặt hàng.")]
        public List<UpdateGoodsReceiptItemRequest> Items { get; set; } = new List<UpdateGoodsReceiptItemRequest>();
    }

    public class UpdateGoodsReceiptItemRequest
    {
        [Required]
        public Guid PurchaseOrderItemId { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng chấp nhận phải lớn hơn hoặc bằng 0")]
        public int AcceptedQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng hư hỏng phải lớn hơn hoặc bằng 0")]
        public int DamagedQuantity { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng sai hàng phải lớn hơn hoặc bằng 0")]
        public int WrongItemQuantity { get; set; }
    }

    public class GoodsReceiptDto
    {
        public Guid Id { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderCode { get; set; } = string.Empty;
        public Guid ReceivedByUserId { get; set; }
        public string ReceivedByUserName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string? Note { get; set; }
        public string? ImageProofUrl { get; set; }

        public List<GoodsReceiptItemDto> Items { get; set; } = new List<GoodsReceiptItemDto>();
    }

    public class GoodsReceiptItemDto
    {
        public Guid Id { get; set; }
        public Guid PurchaseOrderItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;  // Product.Name hoặc Material.Name
        public string ItemSku { get; set; } = string.Empty;   // Product.Sku hoặc rỗng
        public string ItemType { get; set; } = "Product";     // "Product" hoặc "Material"
        
        public int AcceptedQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public int ExcessQuantity { get; set; }
        public int ShortQuantity { get; set; }
        public int WrongItemQuantity { get; set; }
        
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Note { get; set; }
    }
}
