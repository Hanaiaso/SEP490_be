namespace VietTien.API.Models
{
    /// <summary>
    /// Ghi nhận từng lô hàng lỗi nhập vào vùng cách ly Quarantine.
    /// Được tạo khi kho tiếp nhận hàng đổi trả từ khách.
    /// </summary>
    public enum QuarantineStatus
    {
        Waiting,           // Chờ QA kiểm tra
        ApprovedAvailable, // QA duyệt: hàng đạt, chuyển về Available
        ApprovedDamaged    // QA duyệt: hàng hỏng, chuyển sang Damaged
    }

    public class QuarantineLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string QuarantineCode { get; set; } = string.Empty; // QZ-yyyyMMdd-XXXX

        public Guid? OrderId { get; set; }
        public Guid? GoodsReceiptItemId { get; set; } // Liên kết với hàng lỗi từ phiếu nhập kho
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public Guid? InventoryId { get; set; }  // Inventory record bị ảnh hưởng

        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty; // Mô tả lý do trả hàng

        public QuarantineStatus Status { get; set; } = QuarantineStatus.Waiting;

        public Guid ReceivedByUserId { get; set; }   // Nhân viên kho tiếp nhận
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // QA dispatch fields (cần ký duyệt quản lý)
        public Guid? DispatchedByUserId { get; set; } // Quản lý ký duyệt
        public DateTime? DispatchedAt { get; set; }
        public string? DispatchNotes { get; set; }

        // Xử lý từng phần (vd 5 nhập cách ly, chỉ 1 hỏng): log gốc còn lại Quantity của phần CHƯA xử lý
        // (vẫn Waiting), phần đã xử lý được tách thành 1 log con trỏ về log gốc qua field này.
        public Guid? ParentQuarantineLogId { get; set; }

        // Navigation Properties
        public Order? Order { get; set; }
        public GoodsReceiptItem? GoodsReceiptItem { get; set; }
        public Product? Product { get; set; }
        public Material? Material { get; set; }
        public Inventory? Inventory { get; set; }
        public User ReceivedByUser { get; set; } = null!;
        public User? DispatchedByUser { get; set; }
        public QuarantineLog? ParentQuarantineLog { get; set; }
    }
}
