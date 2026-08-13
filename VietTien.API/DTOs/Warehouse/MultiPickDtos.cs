using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Warehouse
{
    // FUL-08: gộp pick nhiều đơn — WarehouseStaff đề xuất, SalesManager duyệt, sau đó mới thực thi.
    public class MultiPickOrderIdsRequestDto
    {
        [Required]
        [MinLength(2, ErrorMessage = "Gộp pick cần ít nhất 2 đơn hàng.")]
        public List<Guid> OrderIds { get; set; } = new();
    }

    public class MultiPickApprovalDto
    {
        public Guid Id { get; set; }
        public List<Guid> OrderIds { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public Guid RequestedByUserId { get; set; }
        public DateTime RequestedAt { get; set; }
        public Guid? DecidedByUserId { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
    }

    public class MultiPickDecisionRequestDto
    {
        public bool Approved { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    public class MultiPickExecuteResultDto
    {
        public Guid ApprovalId { get; set; }
        public int PickTasksAccepted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
