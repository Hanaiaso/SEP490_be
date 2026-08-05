namespace VietTien.API.Models
{
    // Nhật ký kiểm toán dùng chung cho toàn hệ thống Admin: chỉ ghi (insert-only),
    // không có endpoint hay code path nào được phép Update/Delete bản ghi này.
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string EntityName { get; set; } = string.Empty; // "SystemConfig", "User", "Endpoint", ...
        public string EntityId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // CREATE, UPDATE, ROLE_CHANGE, STATUS_CHANGE, CONFIG_CHANGE, PERMISSION_DENIED...

        // Snapshot thông tin người thực hiện tại thời điểm ghi log (không phụ thuộc dữ liệu User hiện tại)
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ActorRole { get; set; }

        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }

        public string? Reason { get; set; }
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
