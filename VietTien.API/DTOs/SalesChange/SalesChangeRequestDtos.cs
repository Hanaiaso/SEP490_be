using VietTien.API.Models;

namespace VietTien.API.DTOs.SalesChange
{
    // ─── Customer ─────────────────────────────────────────────────────────────

    /// <summary>Form tạo yêu cầu đổi Sale (multipart — kèm file bằng chứng)</summary>
    public class CreateSalesChangeRequestDto
    {
        public string Reason { get; set; } = string.Empty;
        public string ProblemDescription { get; set; } = string.Empty;
        public Guid? DesiredSalesStaffId { get; set; }
        public List<IFormFile>? Files { get; set; }
    }

    public class AdditionalInfoDto
    {
        public string Info { get; set; } = string.Empty;
    }

    /// <summary>Sale phụ trách hiện tại của khách (hiển thị trong Profile khách)</summary>
    public class MyAssignedSaleDto
    {
        public Guid? SalesStaffId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime? AssignedAt { get; set; }
    }

    public class SalesOptionDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
    }

    // ─── Chung (list/detail) ──────────────────────────────────────────────────

    public class SalesChangeRequestListItemDto
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string CurrentSalesStaffName { get; set; } = string.Empty;
        public string? DesiredSalesStaffName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool HasExplanation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    public class SalesChangeRequestDetailDto : SalesChangeRequestListItemDto
    {
        public Guid CustomerProfileId { get; set; }
        public Guid CurrentSalesStaffId { get; set; }
        public Guid? DesiredSalesStaffId { get; set; }
        public string ProblemDescription { get; set; } = string.Empty;
        public List<string> EvidenceUrls { get; set; } = new();
        public DateTime? ExplanationRequestedAt { get; set; }
        public string? SaleExplanation { get; set; }
        public List<string> SaleExplanationFileUrls { get; set; } = new();
        public DateTime? SaleExplainedAt { get; set; }
        public string? ManagerNote { get; set; }
        public string? ReviewedByName { get; set; }
        public Guid? NewSalesStaffId { get; set; }
        public string? NewSalesStaffName { get; set; }
        public string? OverrideReason { get; set; }
        public string? CustomerAdditionalInfo { get; set; }
        public List<OrderDecisionResultDto> OrderDecisions { get; set; } = new();
    }

    public class OrderDecisionResultDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public bool TransferToNewSale { get; set; }
        public string? Note { get; set; }
    }

    // ─── SalesStaff (giải trình) ──────────────────────────────────────────────

    /// <summary>Form giải trình của Sale (multipart — kèm file đính kèm)</summary>
    public class SaleExplanationDto
    {
        public string Explanation { get; set; } = string.Empty;
        public List<IFormFile>? Files { get; set; }
    }

    // ─── SalesManager ─────────────────────────────────────────────────────────

    public class SalesChangeRequestQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; } // Pending | MoreInfoRequested | Approved | Rejected | Cancelled
    }

    public class ManagerNoteDto
    {
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Dữ liệu hỗ trợ Manager rà soát (Bước 4): đơn đang chạy + workload các Sale</summary>
    public class ReviewContextDto
    {
        public List<RunningOrderDto> RunningOrders { get; set; } = new();
        public List<SalesWorkloadDto> SalesCandidates { get; set; } = new();
    }

    public class RunningOrderDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string DeliveryStatus { get; set; } = string.Empty;
        public decimal FinalPayment { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsInDelivery { get; set; }
    }

    public class SalesWorkloadDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int CustomerCount { get; set; }
        public int OpenOrderCount { get; set; }
        public bool IsActive { get; set; }   // theo RoundRobinParticipant.IsActive (mặc định true nếu chưa vào pool)
        public bool IsDesired { get; set; }  // là Sale khách mong muốn
        public bool IsCurrent { get; set; }  // là Sale hiện tại (không được chọn lại)
    }

    public class ApproveSalesChangeRequestDto
    {
        public Guid NewSalesStaffId { get; set; }

        /// <summary>Bắt buộc khi chọn Sale khác với Sale khách mong muốn</summary>
        public string? OverrideReason { get; set; }

        /// <summary>Quyết định giữ/chuyển từng đơn đang chạy — phải phủ đúng toàn bộ đơn đang chạy</summary>
        public List<OrderDecisionDto> OrderDecisions { get; set; } = new();
    }

    public class OrderDecisionDto
    {
        public Guid OrderId { get; set; }
        public bool TransferToNewSale { get; set; }

        /// <summary>Bắt buộc khi chuyển đơn đang giao (InDelivery)</summary>
        public string? Note { get; set; }
    }

    public class SalesChangeRequestPagedResultDto
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<SalesChangeRequestListItemDto> Items { get; set; } = new();
    }
}
