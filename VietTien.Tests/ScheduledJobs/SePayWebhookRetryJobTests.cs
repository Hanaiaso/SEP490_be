using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// Sheet: ScheduledJobs — L1-SJOB-09..10 (SePayWebhookRetryJob).
    /// Job chỉ lấy log Status = Failed VÀ AttemptCount &lt; WebhookLogService.MaxAttempts (5);
    /// Processed và Abandoned đều bị bỏ qua — Abandoned là trạng thái dừng vĩnh viễn.
    /// </summary>
    public class SePayWebhookRetryJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IWebhookLogService> _webhookLogService = new();
        private readonly SePayWebhookRetryJob _sut;

        public SePayWebhookRetryJobTests()
        {
            _sut = new SePayWebhookRetryJob(_db, _webhookLogService.Object);
            _webhookLogService.Setup(w => w.RetryAsync(It.IsAny<Guid>())).ReturnsAsync(new WebhookLogDto());
        }

        // L1-SJOB-09 | EP-Valid | Chỉ retry log Failed chưa cạn lượt; bỏ qua Processed và Abandoned
        [Fact]
        public async Task L1_SJOB_09_OnlyRetriesFailedLogsBelowMaxAttempts()
        {
            var processed = TestData.WebhookLog(w => w.Status = WebhookLogStatus.Processed);
            var retryable = TestData.WebhookLog(w => { w.Status = WebhookLogStatus.Failed; w.AttemptCount = 2; });
            var abandoned = TestData.WebhookLog(w => { w.Status = WebhookLogStatus.Abandoned; w.AttemptCount = 5; });
            _db.WebhookLogs.AddRange(processed, retryable, abandoned);
            _db.SaveChanges();

            var count = await _sut.RunAsync(CancellationToken.None);

            count.Should().Be(1);
            _webhookLogService.Verify(w => w.RetryAsync(retryable.Id), Times.Once);
            _webhookLogService.Verify(w => w.RetryAsync(processed.Id), Times.Never);
            _webhookLogService.Verify(w => w.RetryAsync(abandoned.Id), Times.Never, "Abandoned là trạng thái dừng vĩnh viễn");
        }

        // L1-SJOB-10 | Idempotency | Log đã Processed -> job không gọi lại, không sinh giao dịch thanh toán thứ 2
        [Fact]
        public async Task L1_SJOB_10_ProcessedLog_IsNeverRetried()
        {
            var processed = TestData.WebhookLog(w => { w.Status = WebhookLogStatus.Processed; w.ProcessedAt = DateTime.UtcNow; });
            _db.WebhookLogs.Add(processed);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);
            await _sut.RunAsync(CancellationToken.None); // chạy thêm 1 chu kỳ nữa

            _webhookLogService.Verify(w => w.RetryAsync(It.IsAny<Guid>()), Times.Never,
                "đơn đã Paid — không được xử lý lại để tránh PaymentTransaction thứ 2");
        }

        // L1-SJOB-09 (biên) | BVA | AttemptCount = MaxAttempts - 1 vẫn retry; = MaxAttempts thì không
        [Theory]
        [InlineData(4, true)]
        [InlineData(5, false)]
        public async Task L1_SJOB_09_RespectsMaxAttemptsBoundary(int attemptCount, bool expectRetry)
        {
            var log = TestData.WebhookLog(w => { w.Status = WebhookLogStatus.Failed; w.AttemptCount = attemptCount; });
            _db.WebhookLogs.Add(log);
            _db.SaveChanges();

            await _sut.RunAsync(CancellationToken.None);

            _webhookLogService.Verify(w => w.RetryAsync(log.Id), expectRetry ? Times.Once() : Times.Never());
        }
    }
}
