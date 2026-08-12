using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-MarketingReturnAPI</c> — MKT-01..11, RET-01..06.
    ///
    /// Ánh xạ chính: {id}/approve -> {id}/decision; {id}/publish -> {id}/publish-now;
    /// {id}/make-callback -> {id}/webhook-callback (header x-make-secret);
    /// POST /api/orders/{id}/return-exchange -> /api/orders/{id}/exchange-request.
    /// </summary>
    public class L3MarketingReturnApiTests : L3TestBase
    {
        public L3MarketingReturnApiTests(L3SqlFixture factory) : base(factory) { }

        private async Task<MarketingPost> SeedPostAsync(MarketingPostStatus status, Guid? authorId = null)
        {
            var post = new MarketingPost
            {
                Id = Guid.NewGuid(),
                Code = "MP-L3-" + Guid.NewGuid().ToString("N")[..6],
                ProductId = L3Seed.ProductTapeTrongId,
                CreatedByUserId = authorId ?? L3Seed.SalesStaffId,
                PromptUsed = "Bai dang thu nghiem L3",
                GeneratedCaption = "Caption goc",
                GeneratedImageUrl = "https://example.invalid/a.png",
                SelectedImageUrl = "https://example.invalid/a.png",
                EditedCaption = "Caption da sua",
                Status = status,
            };
            await SeedAsync(db => { db.MarketingPosts.Add(post); return Task.CompletedTask; });
            return post;
        }

        // ── Block: Marketing — duyệt & đăng (FT-10) ───────────────────────────────────────────

        /// MKT-01 | Input-Domain-Happy | FT-10 AC-01; BR-046
        /// Sales Staff tạo bài -> bài ở trạng thái nháp, CHƯA có tham chiếu bài Facebook.
        [Fact]
        public async Task L3_MKT_01_CreatePost_StartsAsDraft_NoExternalPostId()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.PostAsJsonAsync("/api/marketing-posts", new
            {
                ProductId = L3Seed.ProductTapeTrongId,
                PromptUsed = "Quang cao bang keo",
                GeneratedImageUrl = "https://example.invalid/a.png",
                GeneratedCaption = "Caption AI",
                SelectedImageUrl = "https://example.invalid/a.png",
                EditedCaption = "Caption da chinh sua",
            });

            res.IsSuccessStatusCode.Should().BeTrue(
                $"Sales Staff phải tạo được bài ({(int)res.StatusCode}: {await ReadMessageAsync(res)})");

            var post = await QueryAsync(db => db.MarketingPosts
                .OrderByDescending(p => p.CreatedAt).FirstAsync());
            post.Status.Should().Be(MarketingPostStatus.Draft, "bài mới phải ở trạng thái nháp");
            post.ExternalPostId.Should().BeNullOrEmpty("chưa đăng nên chưa có ID bài Facebook");
            Factory.MakeWebhook.Triggered.Should().BeEmpty("tạo bài KHÔNG được gọi webhook đăng bài");
        }

        /// MKT-02 | Input-Domain-Error | FT-10 NAC-02; BR-046; NFR-SEC03
        /// Tác giả (Sales Staff) tự duyệt bài của mình -> 403, trạng thái không đổi.
        [Fact]
        public async Task L3_MKT_02_SelfApprove_ByAuthorSalesStaff_Forbidden()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Submitted, authorId: L3Seed.SalesStaffId);
            var author = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await author.PostAsJsonAsync($"/api/marketing-posts/{post.Id}/decision",
                new { Action = "Approve" });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "tác giả không được tự duyệt bài");
            (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .Status.Should().Be(MarketingPostStatus.Submitted, "trạng thái không đổi");
        }

        /// MKT-03 | Input-Domain-Error | FT-10 NAC-01; BR-046
        /// Đăng bài đang ở trạng thái NHÁP (chưa duyệt) -> bị từ chối, KHÔNG gọi webhook đăng bài.
        [Fact]
        public async Task L3_MKT_03_PublishDraftPost_Rejected_NoWebhookCalled()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Draft);
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/marketing-posts/{post.Id}/publish-now", new { });

            res.IsSuccessStatusCode.Should().BeFalse("bài chưa duyệt thì không được đăng");
            Factory.MakeWebhook.Triggered.Should().BeEmpty("KHÔNG được gọi webhook đăng bài");
            (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .ExternalPostId.Should().BeNullOrEmpty();
        }

        /// MKT-04 | Input-Domain-Happy | FT-10 AC-03; NFR-P07
        /// Duyệt bài đã Submitted -> chuyển lịch đăng và webhook được gọi ĐÚNG 1 LẦN.
        [Fact]
        public async Task L3_MKT_04_ApproveSubmittedPost_SchedulesAndTriggersWebhookOnce()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Submitted);
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/marketing-posts/{post.Id}/decision",
                new { Action = "Approve", ScheduledTime = DateTime.UtcNow.AddHours(2) });

            res.IsSuccessStatusCode.Should().BeTrue($"duyệt bài phải thành công ({await ReadMessageAsync(res)})");
            (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .Status.Should().Be(MarketingPostStatus.Scheduled);
            Factory.MakeWebhook.Triggered.Should().HaveCount(1, "webhook đăng bài phải được gọi đúng 1 lần");
        }

        /// MKT-05 | Idempotency | FT-10 NAC-05; BR-029; BR-049
        /// Gửi lại callback thành công đã xử lý -> không tạo bản ghi đăng thứ 2, external post ID giữ nguyên.
        [Fact]
        public async Task L3_MKT_05_ReplaySuccessCallback_Idempotent_ExternalPostIdUnchanged()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Posting);
            var anonymous = AnonymousClient();

            async Task<HttpResponseMessage> CallbackAsync(string externalId)
            {
                var req = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/marketing-posts/{post.Id}/webhook-callback")
                {
                    Content = JsonContent.Create(new { Status = "Success", ExternalPostId = externalId }),
                };
                req.Headers.Add("x-make-secret", L3SqlFixture.MakeCallbackSecret);
                return await anonymous.SendAsync(req);
            }

            var first = await CallbackAsync("FB-POST-001");
            var externalAfterFirst = (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .ExternalPostId;

            var second = await CallbackAsync("FB-POST-999"); // cố ghi đè bằng ID khác

            first.IsSuccessStatusCode.Should().BeTrue("callback hợp lệ lần đầu phải được xử lý");
            second.IsSuccessStatusCode.Should().BeTrue("callback lặp vẫn ack 200, trả kết quả gốc");
            externalAfterFirst.Should().Be("FB-POST-001", "lần đầu phải ghi được ID bài đã đăng");
            (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .ExternalPostId.Should().Be(externalAfterFirst,
                    "callback lặp KHÔNG được ghi đè ID bài đã đăng");
        }

        /// MKT-06 | Input-Domain-Error | FT-10 AC-04; BR-049; FT-09 AC-05
        /// Callback báo LỖI từ Facebook -> bài đánh dấu thất bại, giữ lịch sử lỗi.
        [Fact]
        public async Task L3_MKT_06_FailureCallback_MarksPostFailed_KeepsErrorHistory()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Posting);

            var req = new HttpRequestMessage(HttpMethod.Post,
                $"/api/marketing-posts/{post.Id}/webhook-callback")
            {
                Content = JsonContent.Create(new { Status = "Failed", ErrorMessage = "Facebook tu choi noi dung" }),
            };
            req.Headers.Add("x-make-secret", L3SqlFixture.MakeCallbackSecret);

            var res = await AnonymousClient().SendAsync(req);

            ((int)res.StatusCode).Should().BeLessThan(500);
            var updated = await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id));
            updated.Status.Should().Be(MarketingPostStatus.PublishFailed, "bài phải được đánh dấu thất bại");
            updated.PublishErrorMessage.Should().Contain("Facebook", "phải giữ lại lý do thất bại");
        }

        /// MKT-07 | BVA | FT-10 BV-02; NAC-03
        /// Biên thời điểm lên lịch: quá khứ -> từ chối; tương lai -> chấp nhận.
        [Theory]
        [InlineData(-60, false)] // now - 60s
        [InlineData(+60, true)]  // now + 60s
        public async Task L3_MKT_07_ScheduleTimeBoundary_PastRejected_FutureAccepted(
            int offsetSeconds, bool shouldSucceed)
        {
            var post = await SeedPostAsync(MarketingPostStatus.Submitted);
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/marketing-posts/{post.Id}/decision", new
            {
                Action = "Approve",
                ScheduledTime = DateTime.UtcNow.AddSeconds(offsetSeconds),
            });

            if (shouldSucceed)
            {
                res.IsSuccessStatusCode.Should().BeTrue($"lịch tương lai phải hợp lệ ({await ReadMessageAsync(res)})");
            }
            else
            {
                res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "không được lên lịch vào quá khứ");
                (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                    .Status.Should().Be(MarketingPostStatus.Submitted, "lịch cũ không đổi");
            }
        }

        /// MKT-08 | BVA | FT-10 BV-01; NAC-03
        /// Biên hàng đợi lịch đăng: 29 -> duyệt được (thành 30); 30 -> bị chặn (MAX_SCHEDULED_POSTS = 30).
        [Theory]
        [InlineData(29, true)]
        [InlineData(30, false)]
        public async Task L3_MKT_08_ScheduleQueueLimitBoundary_30Posts(int existingScheduled, bool shouldSucceed)
        {
            await SeedAsync(db =>
            {
                for (var i = 0; i < existingScheduled; i++)
                {
                    db.MarketingPosts.Add(new MarketingPost
                    {
                        Id = Guid.NewGuid(),
                        Code = $"MP-Q-{i:D3}",
                        ProductId = L3Seed.ProductTapeTrongId,
                        CreatedByUserId = L3Seed.SalesStaffId,
                        PromptUsed = "queue filler",
                        Status = MarketingPostStatus.Scheduled,
                        ScheduledTime = DateTime.UtcNow.AddDays(1),
                    });
                }
                return Task.CompletedTask;
            });

            var post = await SeedPostAsync(MarketingPostStatus.Submitted);
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync($"/api/marketing-posts/{post.Id}/decision",
                new { Action = "Approve", ScheduledTime = DateTime.UtcNow.AddHours(3) });

            if (shouldSucceed)
                res.IsSuccessStatusCode.Should().BeTrue($"hàng đợi {existingScheduled} bài vẫn còn chỗ");
            else
                res.IsSuccessStatusCode.Should().BeFalse($"hàng đợi đã đủ {existingScheduled} bài, phải chặn");
        }

        /// MKT-09 | Input-Domain-Error | FT-10 NAC-04; NFR-SEC07  ->  nhóm C
        /// Upload media riêng cho bài chưa có endpoint.
        [Fact]
        public async Task L3_MKT_09_UploadMedia_EndpointNotImplemented()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Draft);
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            using var content = new MultipartFormDataContent();
            var exe = new ByteArrayContent(new byte[] { 0x4D, 0x5A }); // header MZ của file .exe
            exe.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(exe, "file", "malware.png");

            var res = await sales.PostAsync($"/api/marketing-posts/{post.Id}/media", content);

            res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            Factory.Cloudinary.Uploaded.Should().BeEmpty("không có file nào được lưu trữ");
        }

        /// MKT-10 | BVA | FT-10 BV-03; NAC-04; NFR-SEC07  ->  nhóm C
        [Fact]
        public async Task L3_MKT_10_MediaSizeLimit_EndpointNotImplemented()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Draft);
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(new byte[1024]), "file", "big.png");

            (await sales.PostAsync($"/api/marketing-posts/{post.Id}/media", content))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        /// MKT-11 | Input-Domain-Happy | FT-10 AC-05; BR-003  ->  nhóm C
        /// Endpoint tra chỉ số bài đăng chưa có; các cột đếm đã tồn tại trong model.
        [Fact]
        public async Task L3_MKT_11_PostMetrics_EndpointNotImplemented_CountersExistInModel()
        {
            var post = await SeedPostAsync(MarketingPostStatus.Success);
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            (await admin.GetAsync($"/api/marketing-posts/{post.Id}/metrics"))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

            // Bài vẫn đọc được qua endpoint chi tiết, và các cột chỉ số tồn tại (mặc định 0).
            var detail = await admin.GetAsync($"/api/marketing-posts/{post.Id}");
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            (await QueryAsync(db => db.MarketingPosts.SingleAsync(p => p.Id == post.Id)))
                .ReachCount.Should().Be(0);
        }

        // ── Block: Đổi/trả & thu hồi (FT-08) ──────────────────────────────────────────────────

        /// <summary>Đơn đã giao của một khách, sẵn sàng cho yêu cầu đổi/trả.</summary>
        private async Task<(HttpClient client, Guid orderId, Product product)> ArrangeDeliveredOrderAsync()
        {
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAddressAsync(profile.Id);
            var (product, _) = await SeedSellableProductAsync(100_000m, 100);
            await SeedCartAsync(profile.Id, null, (product.Id, 2, 100_000m));

            var placed = await ReadJsonAsync(await client.PostAsJsonAsync("/api/orders/place-order",
                new { PaymentMethod = PaymentMethod.COD }));
            var orderId = placed.GetProperty("orderId").GetGuid();

            await SeedAsync(async db =>
            {
                var o = await db.Orders.SingleAsync(x => x.Id == orderId);
                // "Đã giao" = đơn hoàn tất + DeliveryStatus.Delivered (Delivered nằm ở DeliveryStatus,
                // không phải OrderStatus).
                o.OrderStatus = OrderStatus.Completed;
                o.DeliveryStatus = DeliveryStatus.Delivered;
                o.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                o.PaymentStatus = PaymentStatus.Paid;
            });

            return (client, orderId, product);
        }

        /// RET-01 | Input-Domain-Happy | FT-08 AC-05
        /// Khách tạo yêu cầu đổi/trả cho đơn đã giao -> yêu cầu ở trạng thái chờ duyệt, tồn kho KHÔNG đổi.
        [Fact]
        public async Task L3_RET_01_CreateReturnExchangeRequest_OnDeliveredOrder_InventoryUnchanged()
        {
            var (client, orderId, product) = await ArrangeDeliveredOrderAsync();
            var stockBefore = (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity;

            var res = await client.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request", new
            {
                Reason = "Hang bi loi",
                ReturnItems = new[] { new { ProductId = product.Id, Quantity = 1 } },
            });

            res.IsSuccessStatusCode.Should().BeTrue(
                $"khách phải tạo được yêu cầu đổi/trả ({(int)res.StatusCode}: {await ReadMessageAsync(res)})");
            (await QueryAsync(db => db.ReturnExchangeRequests.CountAsync(r => r.OrderId == orderId)))
                .Should().Be(1);
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(stockBefore, "chưa thu hồi thì tồn kho không được đổi");
        }

        /// RET-02 | BVA | FT-08 BV-03
        /// Biên số lượng trả: 0 và vượt số đã giao -> từ chối; đúng số đã giao -> chấp nhận.
        [Theory]
        [InlineData(0, false)]
        [InlineData(2, true)]   // đã giao 2
        [InlineData(3, false)]  // vượt số đã giao
        public async Task L3_RET_02_ReturnQuantityBoundary(int quantity, bool shouldSucceed)
        {
            var (client, orderId, product) = await ArrangeDeliveredOrderAsync();

            var res = await client.PostAsJsonAsync($"/api/orders/{orderId}/exchange-request", new
            {
                Reason = "Kiem tra bien so luong",
                ReturnItems = new[] { new { ProductId = product.Id, Quantity = quantity } },
            });

            if (shouldSucceed)
            {
                res.IsSuccessStatusCode.Should().BeTrue($"trả đúng {quantity} phải được chấp nhận");
            }
            else
            {
                res.IsSuccessStatusCode.Should().BeFalse($"số lượng {quantity} phải bị từ chối");
                (await QueryAsync(db => db.ReturnExchangeRequests.CountAsync(r => r.OrderId == orderId)))
                    .Should().Be(0, "không tạo yêu cầu cho case bị từ chối");
            }
        }

        /// RET-03 | Input-Domain-Error | FT-08 NAC-05; NFR-SEC03
        /// Sales Staff tự duyệt yêu cầu đổi/trả -> 403 (chỉ Sales Manager/Admin).
        [Fact]
        public async Task L3_RET_03_ProcessReturnExchange_BySalesStaff_Forbidden()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.PostAsJsonAsync(
                $"/api/orders/exchange-request/{Guid.NewGuid()}/process", new { IsApproved = true });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// RET-04 | Input-Domain-Error | FT-08 NAC-05; SRS §4.4.3.2
        /// Yêu cầu huỷ đơn khi đơn ĐANG GIAO -> bị từ chối theo state guard.
        [Fact]
        public async Task L3_RET_04_RequestCancel_WhileInDelivery_Rejected()
        {
            var (client, orderId, _) = await ArrangeDeliveredOrderAsync();
            await SeedAsync(async db =>
            {
                var o = await db.Orders.SingleAsync(x => x.Id == orderId);
                o.DeliveryStatus = DeliveryStatus.InDelivery;
            });

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var res = await sales.PostAsJsonAsync($"/api/delivery/{orderId}/request-cancel",
                new { Reason = "Khach doi y" });

            res.IsSuccessStatusCode.Should().BeFalse("đơn đang giao thì không được tạo yêu cầu huỷ");
            (await QueryAsync(db => db.Orders.SingleAsync(o => o.Id == orderId)))
                .DeliveryStatus.Should().Be(DeliveryStatus.InDelivery, "trạng thái giao hàng không đổi");
        }

        /// RET-05 | Input-Domain-Happy | FT-08 AC-05; BR-019
        /// Xác nhận thu hồi: đúng vai trò được phép; hàng thu về KHÔNG làm tăng tồn khả dụng ngay
        /// (phải qua khu cách ly).
        [Fact]
        public async Task L3_RET_05_ConfirmPickup_RoleGate_AvailableStockNotIncreased()
        {
            var (_, _, product) = await ArrangeDeliveredOrderAsync();
            var availableBefore = (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .AvailableQuantity;

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var res = await sales.PostAsJsonAsync(
                $"/api/delivery/pickups/{Guid.NewGuid()}/confirm", new { Items = Array.Empty<object>() });

            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "Sales Staff phải qua được cổng phân quyền");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .AvailableQuantity.Should().Be(availableBefore, "tồn khả dụng không được tăng");

            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.PostAsJsonAsync(
                    $"/api/delivery/pickups/{Guid.NewGuid()}/confirm", new { Items = Array.Empty<object>() }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "khách hàng không được xác nhận thu hồi");
        }

        /// RET-06 | Input-Domain-Error | FT-08 NAC-04
        /// Xác nhận thu hồi khi CHƯA lên lịch thu hồi -> bị từ chối, tồn kho không đổi.
        [Fact]
        public async Task L3_RET_06_ConfirmPickup_NotScheduled_Rejected()
        {
            var (_, _, product) = await ArrangeDeliveredOrderAsync();
            var before = (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity;

            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);
            var res = await sales.PostAsJsonAsync(
                $"/api/delivery/pickups/{Guid.NewGuid()}/confirm", new { Items = Array.Empty<object>() });

            res.IsSuccessStatusCode.Should().BeFalse("chưa lên lịch thu hồi thì không xác nhận được");
            (await QueryAsync(db => db.Inventories.SingleAsync(i => i.ProductId == product.Id)))
                .OnHandQuantity.Should().Be(before);
        }
    }
}
