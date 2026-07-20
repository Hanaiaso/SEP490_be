namespace VietTien.API.Models
{
    // Nguồn phân bổ khách cho Sale (theo đặc tả v6.0)
    public static class AssignmentSource
    {
        public const string RoundRobin = "ROUND_ROBIN";
        public const string Referral = "REFERRAL";
        public const string ReturningCustomer = "RETURNING_CUSTOMER";
        public const string ManualReassignment = "MANUAL_REASSIGNMENT";
    }

    // Bảng singleton (1 dòng) lưu trạng thái con trỏ Round-robin
    public class RoundRobinState
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Tạm dừng toàn bộ việc gán tự động
        public bool IsPaused { get; set; } = false;

        // Cursor: Sale vừa được gán khách gần nhất; người kế tiếp trong pool sẽ nhận khách tiếp theo
        public Guid? LastAssignedSalesStaffId { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Guid? UpdatedById { get; set; }

        // Navigation Properties
        public User? LastAssignedSalesStaff { get; set; }
        public User? UpdatedBy { get; set; }
    }

    // Pool nhân viên Sale tham gia vòng Round-robin
    public class RoundRobinParticipant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SalesStaffId { get; set; }

        // Tắt để loại Sale khỏi vòng chia (ví dụ nghỉ phép) mà không mất thứ tự
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public User SalesStaff { get; set; } = null!;
    }

    // Lịch sử phân bổ khách hàng cho Sale
    public class CustomerAssignmentHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }
        public Guid SalesStaffId { get; set; }

        // Null = hệ thống tự gán (khi đăng ký); có giá trị = manager thao tác
        public Guid? AssignedById { get; set; }

        // Giá trị thuộc AssignmentSource
        public string Source { get; set; } = AssignmentSource.RoundRobin;

        // Audit before/after: Sale phụ trách TRƯỚC lần gán này (null = lần gán đầu tiên)
        public Guid? PreviousSalesStaffId { get; set; }

        // Lý do thay đổi (bắt buộc với MANUAL_REASSIGNMENT theo LUỒNG 7)
        public string? Note { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public User SalesStaff { get; set; } = null!;
        public User? AssignedBy { get; set; }
        public User? PreviousSalesStaff { get; set; }
    }
}
