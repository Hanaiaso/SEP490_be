namespace VietTien.API.DTOs.Admin
{
    public class WebhookLogQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Source { get; set; }
        public string? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class WebhookLogDto
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string RawPayload { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
