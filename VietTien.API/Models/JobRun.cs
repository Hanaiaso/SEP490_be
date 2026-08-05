namespace VietTien.API.Models
{
    public enum JobRunStatus
    {
        Running,
        Success,
        Failed
    }

    public enum JobTriggerType
    {
        Scheduled,
        Manual
    }

    // Lịch sử chạy của 1 scheduled job (xem IScheduledJob). Dùng chung cho cả lần chạy
    // tự động (Scheduled) lẫn lần Admin bấm "retry" thủ công (Manual) trên System Health.
    public class JobRun
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string JobName { get; set; } = string.Empty;
        public JobRunStatus Status { get; set; } = JobRunStatus.Running;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public int? ItemsProcessed { get; set; }
        public string? ErrorMessage { get; set; }

        public JobTriggerType TriggerType { get; set; } = JobTriggerType.Scheduled;
        public Guid? TriggeredByUserId { get; set; }
    }
}
