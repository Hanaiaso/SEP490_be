namespace VietTien.API.DTOs.Admin
{
    public class AuditLogQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? EntityName { get; set; }
        public string? Action { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? SearchQuery { get; set; } // tìm trong EntityId, ActorEmail, Reason
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class AuditLogDto
    {
        public Guid Id { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ActorRole { get; set; }
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string? Reason { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
