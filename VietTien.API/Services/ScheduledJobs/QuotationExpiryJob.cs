using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Báo giá đã CEO duyệt có hiệu lực ValidUntil (mặc định 7 ngày, xem QuotationService.CeoReviewVersionAsync).
    // Trước đây chỉ được đánh dấu Expired "lazy" khi khách bấm accept/reject sau khi đã hết hạn
    // (QuotationService.CustomerDecisionAsync) — nếu khách không thao tác gì thì báo giá treo mãi ở
    // trạng thái Approved dù đã quá hạn, và không ai được báo. Job này quét định kỳ để tự chuyển
    // trạng thái Expired và thông báo cho Sale phụ trách + khách hàng.
    public class QuotationExpiryJob : IScheduledJob
    {
        public string JobName => "QuotationExpiry";
        public TimeSpan Interval => TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<QuotationExpiryJob> _logger;

        public QuotationExpiryJob(ApplicationDbContext context, INotificationService notificationService, ILogger<QuotationExpiryJob> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var expiredQuotations = await _context.Quotations
                .Include(q => q.Versions)
                .Include(q => q.CustomerProfile)
                .Where(q => q.Status == QuotationStatus.Approved && q.ValidUntil != null && q.ValidUntil < now)
                .ToListAsync(ct);

            var processedCount = 0;

            foreach (var q in expiredQuotations)
            {
                try
                {
                    var version = q.Versions.FirstOrDefault(v => v.Status == QuotationVersionStatus.CeoApproved);
                    if (version != null)
                        version.Status = QuotationVersionStatus.Expired;

                    q.Status = QuotationStatus.Expired;
                    await _context.SaveChangesAsync(ct);

                    if (q.SalesStaffId.HasValue)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_28_QuotationExpired,
                            q.SalesStaffId.Value,
                            "Báo giá đã hết hạn",
                            $"Báo giá cho khách hàng {q.CustomerProfile?.CompanyName} đã hết hạn ({q.ValidUntil:dd/MM/yyyy HH:mm}) do khách chưa quyết định.",
                            q.Id,
                            "Quotation");
                    }

                    if (q.CustomerProfile?.UserId != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_28_QuotationExpired,
                            q.CustomerProfile.UserId,
                            "Báo giá đã hết hạn",
                            "Báo giá gửi cho bạn đã hết hiệu lực. Vui lòng liên hệ Sale phụ trách nếu vẫn muốn tiếp tục.",
                            q.Id,
                            "Quotation");
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng báo giá: 1 báo giá lỗi không được chặn xử lý các báo giá khác.
                    _logger.LogError(ex, "Lỗi xử lý hết hạn báo giá {QuotationId}", q.Id);
                }
            }

            return processedCount;
        }
    }
}
