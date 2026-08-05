namespace VietTien.API.DTOs.Admin
{
    public class JobRunQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? JobName { get; set; }
        public string? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class JobRunDto
    {
        public Guid Id { get; set; }
        public string JobName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public long? DurationMs { get; set; }
        public int? ItemsProcessed { get; set; }
        public string? ErrorMessage { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public Guid? TriggeredByUserId { get; set; }
    }

    public class JobHealthSummaryDto
    {
        public string JobName { get; set; } = string.Empty;
        public string IntervalDescription { get; set; } = string.Empty;
        public JobRunDto? LastRun { get; set; }
        public int TodaySuccessCount { get; set; }
        public int TodayFailureCount { get; set; }
        public bool IsCurrentlyRunning { get; set; }
    }
}
