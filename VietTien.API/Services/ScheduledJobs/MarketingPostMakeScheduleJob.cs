using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    public class MarketingPostMakeScheduleJob : IScheduledJob
    {
        private readonly ApplicationDbContext _context;
        private readonly IMakeWebhookService _makeWebhookService;
        private readonly ILogger<MarketingPostMakeScheduleJob> _logger;

        public string JobName => "MARKETING_POST_MAKE_SCHEDULE";
        public TimeSpan Interval => TimeSpan.FromMinutes(1);

        public MarketingPostMakeScheduleJob(
            ApplicationDbContext context,
            IMakeWebhookService makeWebhookService,
            ILogger<MarketingPostMakeScheduleJob> logger)
        {
            _context = context;
            _makeWebhookService = makeWebhookService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            var duePosts = await _context.MarketingPosts
                .Include(p => p.Product)
                .Where(p => (p.Status == MarketingPostStatus.Scheduled || p.Status == MarketingPostStatus.Approved) 
                            && p.ScheduledTime <= DateTime.UtcNow)
                .ToListAsync(ct);

            if (duePosts.Count == 0) return 0;

            _logger.LogInformation("Tìm thấy {Count} bài viết AI Marketing đến hạn đăng qua Make.com Webhook", duePosts.Count);
            int processedCount = 0;

            foreach (var post in duePosts)
            {
                try
                {
                    post.Status = MarketingPostStatus.Posting;
                    post.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);

                    var triggered = await _makeWebhookService.TriggerPostToMakeAsync(post);
                    if (!triggered)
                    {
                        // Make.com không nhận được request -> sẽ không bao giờ gọi callback,
                        // đánh dấu thất bại ngay thay vì để bài "Posting" treo vĩnh viễn.
                        post.Status = MarketingPostStatus.PublishFailed;
                        post.PublishErrorMessage = "Không thể gửi yêu cầu đăng bài tới Make.com. Vui lòng thử lại.";
                        await _context.SaveChangesAsync(ct);
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi bài viết ID {PostId} tới Make.com", post.Id);
                }
            }

            return processedCount;
        }
    }
}
