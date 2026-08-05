namespace VietTien.API.Models
{
    public enum WebhookLogStatus
    {
        Received,
        Processed,
        Failed,
        Abandoned
    }

    // Nhật ký webhook đến (hiện chỉ SePay) để có thể replay/retry khi xử lý lỗi.
    // KHÔNG lưu token xác thực (bí mật chia sẻ) — retry dùng token cấu hình phía server, xem SePayWebhookRetryJob.
    public class WebhookLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Source { get; set; } = "SePay";
        public string RawPayload { get; set; } = string.Empty; // JSON body, không chứa token
        public WebhookLogStatus Status { get; set; } = WebhookLogStatus.Received;

        public int AttemptCount { get; set; } = 0;
        public string? LastError { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
