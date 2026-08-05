using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.ScheduledJobs
{
    /// <summary>
    /// Sheet: ScheduledJobs — L1-SJOB-14..16 (MarketingPostMakeScheduleJob).
    /// Job chỉ lấy bài Status = Scheduled hoặc Approved VÀ ScheduledTime &lt;= now.
    /// </summary>
    public class MarketingPostMakeScheduleJobTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IMakeWebhookService> _makeWebhook = new();
        private readonly MarketingPostMakeScheduleJob _sut;
        private readonly Product _product;
        private readonly User _author;

        public MarketingPostMakeScheduleJobTests()
        {
            _sut = new MarketingPostMakeScheduleJob(_db, _makeWebhook.Object, NullLogger<MarketingPostMakeScheduleJob>.Instance);
            _makeWebhook.Setup(m => m.TriggerPostToMakeAsync(It.IsAny<MarketingPost>())).ReturnsAsync(true);

            _product = TestData.SeedProduct(_db);
            _author = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(_author);
            _db.SaveChanges();
        }

        private MarketingPost SeedPost(MarketingPostStatus status, DateTime? scheduledTime)
        {
            var post = TestData.MarketingPost(_product.Id, _author.Id, p =>
            {
                p.Status = status;
                p.ScheduledTime = scheduledTime;
            });
            _db.MarketingPosts.Add(post);
            _db.SaveChanges();
            return post;
        }

        // L1-SJOB-14 | EP-Valid | Chỉ đăng bài ĐÃ DUYỆT và ĐÃ tới giờ hẹn
        [Fact]
        public async Task L1_SJOB_14_OnlyPublishesApprovedAndDuePosts()
        {
            var draftDue = SeedPost(MarketingPostStatus.Draft, DateTime.UtcNow.AddMinutes(-5));      // nháp, tới giờ
            var approvedNotDue = SeedPost(MarketingPostStatus.Scheduled, DateTime.UtcNow.AddHours(2)); // đã duyệt, chưa tới giờ
            var approvedDue = SeedPost(MarketingPostStatus.Scheduled, DateTime.UtcNow.AddMinutes(-1)); // đã duyệt, tới giờ

            var count = await _sut.RunAsync(CancellationToken.None);

            count.Should().Be(1);
            _makeWebhook.Verify(m => m.TriggerPostToMakeAsync(It.Is<MarketingPost>(p => p.Id == approvedDue.Id)), Times.Once);
            _makeWebhook.Verify(m => m.TriggerPostToMakeAsync(It.Is<MarketingPost>(p => p.Id == draftDue.Id)), Times.Never);
            _makeWebhook.Verify(m => m.TriggerPostToMakeAsync(It.Is<MarketingPost>(p => p.Id == approvedNotDue.Id)), Times.Never);
        }

        // L1-SJOB-15 | Idempotency | Chạy lại job -> không đăng lặp bài đã gửi ở chu kỳ trước
        [Fact]
        public async Task L1_SJOB_15_RunTwice_DoesNotRepublish()
        {
            var post = SeedPost(MarketingPostStatus.Scheduled, DateTime.UtcNow.AddMinutes(-1));

            await _sut.RunAsync(CancellationToken.None); // -> Posting
            await _sut.RunAsync(CancellationToken.None); // Posting không còn thuộc phạm vi job

            _makeWebhook.Verify(m => m.TriggerPostToMakeAsync(It.Is<MarketingPost>(p => p.Id == post.Id)), Times.Once);
            _db.MarketingPosts.Single(p => p.Id == post.Id).Status.Should().Be(MarketingPostStatus.Posting);
        }

        // L1-SJOB-16 | EP-Valid | Webhook lỗi -> đánh dấu thất bại + lưu lý do, bài KHÔNG bị mất
        [Fact]
        public async Task L1_SJOB_16_WebhookFailure_MarksPublishFailedAndKeepsPost()
        {
            var post = SeedPost(MarketingPostStatus.Scheduled, DateTime.UtcNow.AddMinutes(-1));
            _makeWebhook.Setup(m => m.TriggerPostToMakeAsync(It.IsAny<MarketingPost>())).ReturnsAsync(false);

            await _sut.RunAsync(CancellationToken.None);

            _db.ChangeTracker.Clear();
            var saved = _db.MarketingPosts.Single(p => p.Id == post.Id);
            saved.Status.Should().Be(MarketingPostStatus.PublishFailed, "không được để bài treo mãi ở Posting");
            saved.PublishErrorMessage.Should().NotBeNullOrWhiteSpace();
            _db.MarketingPosts.Should().ContainSingle("bài vẫn còn trong lịch sử");
        }
    }
}
