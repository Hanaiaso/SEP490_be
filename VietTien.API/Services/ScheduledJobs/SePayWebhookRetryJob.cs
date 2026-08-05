using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Retry các webhook SePay xử lý lỗi (WebhookLog.Status=Failed), tối đa WebhookLogService.MaxAttempts lần,
    // dùng chung logic retry-1-item với API "retry thủ công" trên System Health (IWebhookLogService.RetryAsync).
    public class SePayWebhookRetryJob : IScheduledJob
    {
        public string JobName => "SePayWebhookRetry";
        public TimeSpan Interval => TimeSpan.FromMinutes(5);

        private readonly ApplicationDbContext _context;
        private readonly IWebhookLogService _webhookLogService;

        public SePayWebhookRetryJob(ApplicationDbContext context, IWebhookLogService webhookLogService)
        {
            _context = context;
            _webhookLogService = webhookLogService;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var failedLogs = await _context.WebhookLogs
                .Where(w => w.Status == WebhookLogStatus.Failed && w.AttemptCount < WebhookLogService.MaxAttempts)
                .Select(w => w.Id)
                .ToListAsync(ct);

            foreach (var id in failedLogs)
            {
                await _webhookLogService.RetryAsync(id);
            }

            return failedLogs.Count;
        }
    }
}
