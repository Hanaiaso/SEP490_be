using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Marketing;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: MarketingPostService — L1-MKT-01, 02, 04..14. EF InMemory + mock IMakeWebhookService.
    /// ⚠ L1-MKT-03 (Sales Staff tự duyệt bài của mình -> forbidden): chặn vai trò nằm ở
    ///    [Authorize(Roles = "SalesManager,SaleManager,Admin,CEO")] trên MarketingPostController.MakeDecision
    ///    — MakeDecisionAsync không nhận userRole nên không unit-test được ở đây -> đã chuyển sang L3:
    ///    VietTien.IntegrationTests/RoleGateTests.cs (L1_MKT_03_*).
    /// ⚠ MarketingPostDecisionDto dùng string Action ("Approve" | "ApproveNow" | "Rework" | "Reject"),
    ///    không phải enum Decision như doc mô tả; Approve chuyển thẳng sang Scheduled (5).
    /// </summary>
    public class MarketingPostServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IMakeWebhookService> _makeWebhook = new();
        private readonly Mock<INotificationService> _notification = new();
        private readonly MarketingPostService _sut;
        private readonly Product _product;
        private readonly User _salesStaff;
        private readonly User _salesManager;

        public MarketingPostServiceTests()
        {
            _sut = new MarketingPostService(_db, _makeWebhook.Object, _notification.Object);
            _makeWebhook.Setup(m => m.TriggerPostToMakeAsync(It.IsAny<MarketingPost>())).ReturnsAsync(true);

            _product = TestData.SeedProduct(_db);
            _salesStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _salesManager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.AddRange(_salesStaff, _salesManager);
            _db.SaveChanges();
        }

        private MarketingPost SeedPost(MarketingPostStatus status, Action<MarketingPost>? mutate = null)
        {
            var post = TestData.MarketingPost(_product.Id, _salesStaff.Id, p =>
            {
                p.Status = status;
                mutate?.Invoke(p);
            });
            _db.MarketingPosts.Add(post);
            _db.SaveChanges();
            return post;
        }

        private MarketingPost Reload(Guid id)
        {
            _db.ChangeTracker.Clear();
            return _db.MarketingPosts.Single(p => p.Id == id);
        }

        // ── Block: Vòng đời bài đăng ────────────────────────────────────────

        // L1-MKT-01 | EP-Valid | Sales Staff tạo bài -> trạng thái Draft (0), chưa có id bài Facebook
        [Fact]
        public async Task L1_MKT_01_Create_StartsAsDraft()
        {
            var dto = await _sut.CreatePostAsync(new CreateMarketingPostDto
            {
                ProductId = _product.Id,
                PromptUsed = "Giới thiệu ống PVC D21",
                GeneratedCaption = "Ống PVC bền đẹp",
                GeneratedImageUrl = "https://cdn/ai.png",
                EditedCaption = "Ống PVC bền đẹp",
            }, _salesStaff.Id);

            dto.Status.Should().Be(nameof(MarketingPostStatus.Draft));
            dto.ExternalPostId.Should().BeNull("bài nháp chưa từng được đăng");
        }

        // L1-MKT-02 | State-Valid | Gửi duyệt -> Draft chuyển sang Submitted (1)
        [Fact]
        public async Task L1_MKT_02_Submit_MovesDraftToSubmitted()
        {
            var post = SeedPost(MarketingPostStatus.Draft);

            var dto = await _sut.SubmitPostAsync(post.Id, _salesStaff.Id, "SalesStaff");

            dto.Status.Should().Be(nameof(MarketingPostStatus.Submitted));
        }

        // L1-MKT-05 | State-Valid | Sales Manager duyệt -> chuyển sang lịch đăng, lưu người duyệt
        // Lưu ý: code chuyển thẳng Submitted -> Scheduled (5) chứ không dừng ở Approved (2).
        [Fact]
        public async Task L1_MKT_05_Approve_SchedulesPostAndRecordsApprover()
        {
            var post = SeedPost(MarketingPostStatus.Submitted);

            var dto = await _sut.MakeDecisionAsync(post.Id,
                new MarketingPostDecisionDto { Action = "Approve", ScheduledTime = DateTime.UtcNow.AddHours(2) },
                _salesManager.Id);

            dto.Status.Should().Be(nameof(MarketingPostStatus.Scheduled));
            dto.ApprovedByUserId.Should().Be(_salesManager.Id);
            dto.ScheduledTime.Should().NotBeNull();
        }

        // L1-MKT-06 | State-Valid | Từ chối / yêu cầu sửa kèm lý do -> lưu lý do, trả về cho Sales
        [Theory]
        [InlineData("Reject", MarketingPostStatus.Rejected)]
        [InlineData("Rework", MarketingPostStatus.ReworkRequired)]
        public async Task L1_MKT_06_RejectOrRework_StoresReason(string action, MarketingPostStatus expected)
        {
            var post = SeedPost(MarketingPostStatus.Submitted);

            var dto = await _sut.MakeDecisionAsync(post.Id,
                new MarketingPostDecisionDto { Action = action, RejectionReason = "Ảnh chưa đúng nhận diện thương hiệu" },
                _salesManager.Id);

            dto.Status.Should().Be(expected.ToString());
            dto.RejectionReason.Should().Be("Ảnh chưa đúng nhận diện thương hiệu");
        }

        // L1-MKT-07 | EP-Invalid | Sửa nội dung bài ĐÃ duyệt mà không duyệt lại -> chặn
        [Fact]
        public async Task L1_MKT_07_UpdateApprovedPost_IsBlocked()
        {
            var post = SeedPost(MarketingPostStatus.Approved);

            var act = () => _sut.UpdatePostAsync(post.Id,
                new UpdateMarketingPostDto { EditedCaption = "Caption mới chưa qua duyệt", SelectedImageUrl = "https://cdn/x.png" },
                _salesStaff.Id, "SalesStaff");

            await act.Should().ThrowAsync<Exception>("không được giữ phê duyệt cũ cho nội dung mới");
            Reload(post.Id).EditedCaption.Should().NotBe("Caption mới chưa qua duyệt");
        }

        // ── Block: Đăng bài & callback ──────────────────────────────────────

        // L1-MKT-09 | EP-Valid | Callback thành công -> Status = Success (7), lưu id bài ngoài + thời điểm đăng
        [Fact]
        public async Task L1_MKT_09_Callback_Success_StoresExternalId()
        {
            var post = SeedPost(MarketingPostStatus.Posting);

            var dto = await _sut.HandleMakeWebhookCallbackAsync(post.Id,
                new MakeWebhookCallbackDto { Status = "Success", ExternalPostId = "fb_123456789" });

            dto.Status.Should().Be(nameof(MarketingPostStatus.Success));
            dto.ExternalPostId.Should().Be("fb_123456789");
            dto.PublishedAt.Should().NotBeNull();
        }

        // L1-MKT-10 | EP-Valid | Callback thất bại -> PublishFailed (8), lưu lỗi, bài vẫn còn trong lịch sử
        [Fact]
        public async Task L1_MKT_10_Callback_Failure_KeepsPostAndRecordsError()
        {
            var post = SeedPost(MarketingPostStatus.Posting);

            var dto = await _sut.HandleMakeWebhookCallbackAsync(post.Id,
                new MakeWebhookCallbackDto { Status = "Failed", ErrorMessage = "Facebook từ chối nội dung" });

            dto.Status.Should().Be(nameof(MarketingPostStatus.PublishFailed));
            dto.PublishErrorMessage.Should().Be("Facebook từ chối nội dung");
            _db.MarketingPosts.Should().ContainSingle("bài không bị xoá khỏi lịch sử");
        }

        // L1-MKT-11 | Idempotency | Callback gửi trùng -> không đổi trạng thái, không sinh bản ghi đăng thứ 2
        [Fact]
        public async Task L1_MKT_11_DuplicateCallback_DoesNotCreateSecondPublish()
        {
            var post = SeedPost(MarketingPostStatus.Posting);
            var payload = new MakeWebhookCallbackDto { Status = "Success", ExternalPostId = "fb_123456789" };
            await _sut.HandleMakeWebhookCallbackAsync(post.Id, payload);

            var second = await _sut.HandleMakeWebhookCallbackAsync(post.Id, payload);

            second.Status.Should().Be(nameof(MarketingPostStatus.Success));
            second.ExternalPostId.Should().Be("fb_123456789");
            _db.MarketingPosts.Count(p => p.Id == post.Id).Should().Be(1);
            _makeWebhook.Verify(m => m.TriggerPostToMakeAsync(It.IsAny<MarketingPost>()), Times.Never,
                "callback KHÔNG được kích hoạt đăng lại");
        }

        // L1-MKT-12 | EP-Invalid | Callback cho bài KHÔNG ở trạng thái đang đăng -> bỏ qua an toàn
        // 🔴 SPEC GAP v2.2: HandleMakeWebhookCallbackAsync KHÔNG kiểm tra trạng thái nguồn — bài Draft
        // nhảy thẳng sang Success. Test ĐỎ cho tới khi bổ sung guard (BR-049).
        [Fact]
        public async Task L1_MKT_12_Callback_OnNonPostingPost_IsIgnored()
        {
            var post = SeedPost(MarketingPostStatus.Draft);

            await _sut.HandleMakeWebhookCallbackAsync(post.Id, new MakeWebhookCallbackDto { Status = "Success", ExternalPostId = "fb_1" });

            Reload(post.Id).Status.Should().Be(MarketingPostStatus.Draft,
                "bài Draft không được nhảy thẳng sang Success qua callback");
        }

        // L1-MKT-13 | EP-Valid | Đăng bài KHÔNG làm đổi quyền phụ trách khách hàng (BR-003)
        [Fact]
        public async Task L1_MKT_13_Publishing_DoesNotChangeCustomerOwnership()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            var (_, profile) = TestData.SeedCustomer(_db);
            profile.AssignedSalesStaffId = otherSales.Id;
            _db.SaveChanges();

            var post = SeedPost(MarketingPostStatus.Posting); // bài do _salesStaff tạo, không phải otherSales
            await _sut.HandleMakeWebhookCallbackAsync(post.Id, new MakeWebhookCallbackDto { Status = "Success", ExternalPostId = "fb_1" });

            _db.CustomerProfiles.Single(p => p.Id == profile.Id).AssignedSalesStaffId
                .Should().Be(otherSales.Id, "marketing không được đổi ownership khách hàng");
        }

        // ── Block: ⊕ v2.2 — Giới hạn hàng đợi lên lịch ──────────────────────

        // L1-MKT-14 | BVA | Quanh ngưỡng tối đa bài đã lên lịch: 29 -> cho duyệt (thành 30); 30 -> từ chối
        // ⚠ Ngưỡng là HẰNG SỐ MarketingPostService.MAX_SCHEDULED_POSTS = 30, KHÔNG đọc từ
        //   SystemConfig 'MAX_SCHEDULED_MARKETING_POSTS' như doc mô tả. Xem DOC_MISMATCHES.md.
        [Theory]
        [InlineData(29, false)]
        [InlineData(30, true)]
        public async Task L1_MKT_14_ScheduledQueueLimit(int existingScheduled, bool shouldReject)
        {
            for (var i = 0; i < existingScheduled; i++)
                _db.MarketingPosts.Add(TestData.MarketingPost(_product.Id, _salesStaff.Id, p => p.Status = MarketingPostStatus.Scheduled));
            _db.SaveChanges();

            var post = SeedPost(MarketingPostStatus.Submitted);
            var act = () => _sut.MakeDecisionAsync(post.Id,
                new MarketingPostDecisionDto { Action = "Approve", ScheduledTime = DateTime.UtcNow.AddHours(3) },
                _salesManager.Id);

            if (shouldReject)
            {
                await act.Should().ThrowAsync<Exception>("đã đạt giới hạn hàng đợi lên lịch");
                Reload(post.Id).Status.Should().Be(MarketingPostStatus.Submitted);
            }
            else
            {
                await act.Should().NotThrowAsync();
                Reload(post.Id).Status.Should().Be(MarketingPostStatus.Scheduled);
            }
        }
    }
}
