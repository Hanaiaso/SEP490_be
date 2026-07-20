namespace VietTien.API.Models
{
    // Trạng thái yêu cầu đổi Sale phụ trách (LUỒNG 7)
    public enum SalesChangeRequestStatus
    {
        Pending = 0,            // Vừa tạo / đã bổ sung thông tin, chờ Manager xử lý (SLA = CreatedAt)
        MoreInfoRequested = 1,  // Manager yêu cầu khách bổ sung thông tin
        Approved = 2,
        Rejected = 3,
        Cancelled = 4           // Khách tự hủy khi còn Pending/MoreInfoRequested
    }

    // Yêu cầu đổi nhân viên Sale phụ trách do khách hàng tạo (WF-07; CUS-11, MGR-06)
    public class SalesChangeRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }

        // Snapshot Sale phụ trách tại thời điểm tạo yêu cầu
        public Guid CurrentSalesStaffId { get; set; }

        // Sale khách mong muốn chuyển sang (không bắt buộc)
        public Guid? DesiredSalesStaffId { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string ProblemDescription { get; set; } = string.Empty;

        // JSON array các URL bằng chứng trên Cloudinary
        public string? EvidenceUrls { get; set; }

        public SalesChangeRequestStatus Status { get; set; } = SalesChangeRequestStatus.Pending;

        // Gate bảo vệ khách hàng: Sale hiện tại CHỈ thấy được yêu cầu sau khi Manager bấm "Yêu cầu giải trình"
        public DateTime? ExplanationRequestedAt { get; set; }
        public Guid? ExplanationRequestedByUserId { get; set; }

        // Bước 3: giải trình của Sale hiện tại (Sale KHÔNG có quyền sửa/xóa/từ chối yêu cầu của khách)
        public string? SaleExplanation { get; set; }
        public string? SaleExplanationFileUrls { get; set; } // JSON array URL file đính kèm giải trình
        public DateTime? SaleExplainedAt { get; set; }

        // Bước 5: quyết định của Sales Manager
        public Guid? ReviewedByUserId { get; set; }
        public string? ManagerNote { get; set; }        // Lý do từ chối / nội dung yêu cầu bổ sung
        public Guid? NewSalesStaffId { get; set; }      // Sale được gán khi phê duyệt

        // Bắt buộc khi Manager chọn Sale khác với Sale khách mong muốn (desired inactive/quá tải)
        public string? OverrideReason { get; set; }

        public DateTime? ReviewedAt { get; set; }

        // Nội dung khách bổ sung khi MoreInfoRequested (sau đó quay lại Pending)
        public string? CustomerAdditionalInfo { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Đồng thời là mốc bắt đầu SLA
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public User CurrentSalesStaff { get; set; } = null!;
        public User? DesiredSalesStaff { get; set; }
        public User? NewSalesStaff { get; set; }
        public User? ReviewedBy { get; set; }
        public User? ExplanationRequestedBy { get; set; }
        public ICollection<SalesChangeRequestOrderDecision> OrderDecisions { get; set; } = new List<SalesChangeRequestOrderDecision>();
    }

    // Audit quyết định của Manager với từng đơn đang chạy khi phê duyệt (giữ Sale cũ hoàn tất hay chuyển Sale mới)
    public class SalesChangeRequestOrderDecision
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SalesChangeRequestId { get; set; }
        public Guid OrderId { get; set; }

        // false = Sale cũ hoàn tất đơn (KEEP); true = chuyển quyền xử lý cho Sale mới (TRANSFER)
        public bool TransferToNewSale { get; set; }

        // Bắt buộc khi transfer đơn đang giao (DeliveryStatus.InDelivery) — Manager override phải ghi lý do
        public string? Note { get; set; }

        public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public SalesChangeRequest SalesChangeRequest { get; set; } = null!;
        public Order Order { get; set; } = null!;
    }
}
