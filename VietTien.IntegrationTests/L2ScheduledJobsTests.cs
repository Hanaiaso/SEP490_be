using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-ScheduledJobs — 7 job nền chạy trên SQL Server thật.
    ///
    /// R6 — BIÊN THỜI GIAN: codebase không có abstraction thời gian (mọi job đọc thẳng
    /// DateTime.UtcNow), nên KHÔNG test mốc chính xác. Backdate cách ngưỡng một khoảng an toàn.
    /// Mặc định 5 phút mỗi bên; RIÊNG OrderSlaJob dùng ±2 phút vì 3 ngưỡng của nó (25/30/35)
    /// chỉ cách nhau 5 phút — lấy ±5 sẽ nhảy sang ngưỡng kế bên. ±2 phút vẫn lớn hơn độ phân giải
    /// DateTime.UtcNow (~15,6ms) khoảng 7.700 lần nên hoàn toàn tất định.
    /// Biên chính xác đã do L1-SJOB-01/02 chứng minh bằng đồng hồ giả.
    ///
    /// R4 — dùng RawAvailable() (chưa kẹp Math.Max) chứ không assert AvailableQuantity.
    /// R7 — mỗi job có ≥1 case idempotency: 02, 05, 07, 09, 11, 13, 15.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2ScheduledJobsTests : SqlServerTestBase
    {
        public L2ScheduledJobsTests(SqlServerFixture factory) : base(factory) { }

        // ── Helpers ────────────────────────────────────────────────────────────────────────

        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        /// <summary>Ngưỡng giữ tồn SePay đọc từ SystemConfig đã seed (R6 — không hard-code 15).</summary>
        private async Task<int> GetReservationMinutesAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
            var raw = await cfg.GetEffectiveValueAsync("SEPAY_RESERVATION_MINUTES");
            return int.TryParse(raw, out var m) && m > 0 ? m : 15;
        }

        private Task<Inventory> ReloadInventoryAsync(Guid id) =>
            QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == id));

        /// <summary>Đưa dòng Inventory đầu tiên về trạng thái xác định và trả về (productId, inventoryId).</summary>
        private async Task<(Guid ProductId, Guid InventoryId)> SetInventoryAsync(
            int onHand, int reserved = 0, int allocated = 0, int? reorderThreshold = null)
        {
            Guid pid = Guid.Empty, iid = Guid.Empty;
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = onHand;
                inv.ReservedQuantity = reserved;
                inv.AllocatedQuantity = allocated;
                inv.DamagedQuantity = 0;
                inv.QuarantineQuantity = 0;
                inv.InTransitQuantity = 0;
                inv.ReorderThreshold = reorderThreshold;
                pid = inv.ProductId!.Value; iid = inv.Id;

                foreach (var o in await db.Inventories.Where(i => i.ProductId == pid && i.Id != inv.Id).ToListAsync())
                {
                    o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0; o.ReorderThreshold = null;
                }
            });
            return (pid, iid);
        }

        private async Task<Guid> SeedOrderAsync(
            Guid productId, int qty, DateTime createdAt,
            OrderStatus orderStatus, PaymentStatus paymentStatus, PaymentMethod method,
            Action<Order>? mutate = null)
        {
            var id = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                var order = new Order
                {
                    Id = id,
                    CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 1_000_000m,
                    FinalPayment = 1_000_000m,
                    PaymentMethod = method,
                    PaymentStatus = paymentStatus,
                    OrderStatus = orderStatus,
                    CreatedAt = createdAt,
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = productId, Quantity = qty, PriceSnapshot = 1_000_000m, CostSnapshot = 0m }
                    }
                };
                mutate?.Invoke(order);
                db.Orders.Add(order);
            });
            return id;
        }

        private Task<int> CountNotifAsync(Guid refId, NotificationType type) =>
            QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == refId && n.Type == type));

        // ══════════════════ SePayReservationExpiryJob ══════════════════

        // GIVEN  2 đơn SePay: A tạo trong hạn, B tạo quá hạn; Reserved A=3, B=2
        // WHEN   Gọi trực tiếp 1 chu kỳ SePayReservationExpiryJob
        // THEN   A vẫn Reserved=3; B được giải phóng; RawAvailable chỉ +2; không đơn nào âm kho
        [Fact]
        [Trait("TestID", "L2-SJOB-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 BV-01; BR-033")]
        public async Task L2_SJOB_01_ReleasesOnlyExpiredReservation()
        {
            await ResetAsync();
            var minutes = await GetReservationMinutesAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 10, reserved: 5);

            var orderA = await SeedOrderAsync(productId, 3, DateTime.UtcNow.AddMinutes(-(minutes - 5)),
                OrderStatus.Draft, PaymentStatus.Pending, PaymentMethod.SePay);   // trong hạn
            var orderB = await SeedOrderAsync(productId, 2, DateTime.UtcNow.AddMinutes(-(minutes + 5)),
                OrderStatus.Draft, PaymentStatus.Pending, PaymentMethod.SePay);   // quá hạn

            var before = await ReloadInventoryAsync(inventoryId);
            RawAvailable(before).Should().Be(5);

            var processed = await Factory.RunJobAsync<SePayReservationExpiryJob>();

            // (a) kết quả job
            processed.Should().Be(1, "chỉ đơn B quá hạn");

            // (b) DB — scope mới
            var a = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderA));
            var b = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderB));
            a.OrderStatus.Should().Be(OrderStatus.Draft, "đơn A còn trong hạn giữ tồn");
            b.OrderStatus.Should().Be(OrderStatus.Cancelled);

            var after = await ReloadInventoryAsync(inventoryId);
            after.ReservedQuantity.Should().Be(3, "chỉ trả lại 2 đơn vị của đơn B");
            RawAvailable(after).Should().Be(7, "khả dụng THÔ chỉ tăng đúng 2");
            after.OnHandQuantity.Should().BeGreaterOrEqualTo(0);
            RawAvailable(after).Should().BeGreaterOrEqualTo(0);

            // (c) side effect
            b.CancelReason.Should().Contain("SePay");
        }

        // GIVEN  1 reservation quá hạn 3 đơn vị
        // WHEN   Chạy job 2 chu kỳ liên tiếp
        // THEN   Sau chu kỳ 1 khả dụng tăng đúng 3; sau chu kỳ 2 KHÔNG hoàn kép
        [Fact]
        [Trait("TestID", "L2-SJOB-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-029; BR-032")]
        public async Task L2_SJOB_02_IsIdempotentAcrossTwoCycles()
        {
            await ResetAsync();
            var minutes = await GetReservationMinutesAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 10, reserved: 3);
            await SeedOrderAsync(productId, 3, DateTime.UtcNow.AddMinutes(-(minutes + 5)),
                OrderStatus.Draft, PaymentStatus.Pending, PaymentMethod.SePay);

            (await Factory.RunJobAsync<SePayReservationExpiryJob>()).Should().Be(1);
            var afterFirst = await ReloadInventoryAsync(inventoryId);
            RawAvailable(afterFirst).Should().Be(10);

            (await Factory.RunJobAsync<SePayReservationExpiryJob>()).Should().Be(0, "chu kỳ 2 không còn gì để xử lý");
            var afterSecond = await ReloadInventoryAsync(inventoryId);
            RawAvailable(afterSecond).Should().Be(10, "không được hoàn kép");
            afterSecond.ReservedQuantity.Should().Be(0);
            afterSecond.ReservedQuantity.Should().BeGreaterOrEqualTo(0);
        }

        // GIVEN  Đơn A đã Allocated=3 (quá hạn); đơn B Reserved=2 quá hạn
        // WHEN   Chạy SePayReservationExpiryJob
        // THEN   A giữ nguyên Allocated=3 — job KHÔNG cướp hàng đã phân bổ; chỉ B được giải phóng
        [Fact]
        [Trait("TestID", "L2-SJOB-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-033; FT-05 AC-02")]
        public async Task L2_SJOB_03_DoesNotTouchAllocatedStock()
        {
            await ResetAsync();
            var minutes = await GetReservationMinutesAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 10, reserved: 2, allocated: 3);

            // Đơn A đã được xác nhận -> tồn đã chuyển sang Allocated, không còn là "giữ mềm".
            var orderA = await SeedOrderAsync(productId, 3, DateTime.UtcNow.AddMinutes(-(minutes + 5)),
                OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.SePay);
            var orderB = await SeedOrderAsync(productId, 2, DateTime.UtcNow.AddMinutes(-(minutes + 5)),
                OrderStatus.Draft, PaymentStatus.Pending, PaymentMethod.SePay);

            await Factory.RunJobAsync<SePayReservationExpiryJob>();

            var a = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderA));
            var b = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderB));
            a.OrderStatus.Should().Be(OrderStatus.Confirmed, "đơn đã xác nhận không thuộc phạm vi job");
            b.OrderStatus.Should().Be(OrderStatus.Cancelled);

            var inv = await ReloadInventoryAsync(inventoryId);
            inv.AllocatedQuantity.Should().Be(3, "job không được cướp hàng đã phân bổ");
            inv.ReservedQuantity.Should().Be(0);
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);
        }

        // ══════════════════ OrderSlaJob ══════════════════

        // GIVEN  Đơn COD PendingConfirmation tạo cách đây 23 / 27 / 32 / 37 phút
        // WHEN   Chạy 1 chu kỳ OrderSlaJob cho từng mốc
        // THEN   <25' không sinh gì; 25' cảnh báo Sales; 30' escalation Manager; 35' giải phóng reservation
        [Theory]
        [Trait("TestID", "L2-SJOB-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 BV-02; BR-010")]
        [InlineData(23, false, false, false)]
        [InlineData(27, true, false, false)]
        [InlineData(32, false, true, false)]
        [InlineData(37, false, false, true)]
        public async Task L2_SJOB_04_RaisesCorrectSlaActionPerBand(
            int minutesAgo, bool expectSalesAlert, bool expectManagerAlert, bool expectCancelled)
        {
            await ResetAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 10, reserved: 2);

            // Đơn COD cần có Sale phụ trách thì nhánh cảnh báo 25' mới gửi được.
            Guid salesStaffId = Guid.Empty;
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);
            salesStaffId = sales.Id;
            await SeedAsync(async db =>
            {
                var profile = await db.CustomerProfiles.FirstAsync();
                profile.AssignedSalesStaffId = salesStaffId;
            });

            var orderId = await SeedOrderAsync(productId, 2, DateTime.UtcNow.AddMinutes(-minutesAgo),
                OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, PaymentMethod.COD);

            await Factory.RunJobAsync<OrderSlaJob>();

            // (b) DB
            var order = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
            var inv = await ReloadInventoryAsync(inventoryId);

            if (expectCancelled)
            {
                order.OrderStatus.Should().Be(OrderStatus.Cancelled, "quá 35 phút phải giải phóng giữ tồn");
                inv.ReservedQuantity.Should().Be(0);
                RawAvailable(inv).Should().Be(10);
            }
            else
            {
                order.OrderStatus.Should().Be(OrderStatus.PendingConfirmation);
                inv.ReservedQuantity.Should().Be(2, "chưa tới 35 phút thì không được thả tồn");
            }
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);

            // (c) side effect — thông báo đúng mốc, không thừa
            (await CountNotifAsync(orderId, NotificationType.SYS_03_CodUnconfirmed25m))
                .Should().Be(expectSalesAlert ? 1 : 0);
            (await CountNotifAsync(orderId, NotificationType.SYS_04_CodUnconfirmed30m))
                .Should().Be(expectManagerAlert ? 1 : 0);
        }

        // GIVEN  Đơn COD đã được cảnh báo mốc 25 phút ở chu kỳ trước
        // WHEN   Chạy thêm 1 chu kỳ OrderSlaJob
        // THEN   Vẫn đúng 1 bản ghi cảnh báo cho nghiệp vụ này
        [Fact]
        [Trait("TestID", "L2-SJOB-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 AC-02; BR-049")]
        public async Task L2_SJOB_05_SlaAlertIsNotDuplicatedOnSecondCycle()
        {
            await ResetAsync();
            var (productId, _) = await SetInventoryAsync(onHand: 10, reserved: 2);

            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);
            await SeedAsync(async db =>
            {
                var profile = await db.CustomerProfiles.FirstAsync();
                profile.AssignedSalesStaffId = sales.Id;
            });

            var orderId = await SeedOrderAsync(productId, 2, DateTime.UtcNow.AddMinutes(-27),
                OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, PaymentMethod.COD);

            await Factory.RunJobAsync<OrderSlaJob>();
            (await CountNotifAsync(orderId, NotificationType.SYS_03_CodUnconfirmed25m)).Should().Be(1);

            await Factory.RunJobAsync<OrderSlaJob>();
            (await CountNotifAsync(orderId, NotificationType.SYS_03_CodUnconfirmed25m))
                .Should().Be(1, "chu kỳ thứ hai không được nhân bản cảnh báo");
        }

        // ══════════════════ SePayWebhookRetryJob ══════════════════

        // GIVEN  Đơn đã Paid qua webhook có đúng 1 PaymentTransaction; WebhookLog tương ứng Failed
        // WHEN   Chạy SePayWebhookRetryJob
        // THEN   Vẫn đúng 1 PaymentTransaction; log chuyển Processed; không thông báo lặp
        [Fact]
        [Trait("TestID", "L2-SJOB-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 NAC-02; BR-029")]
        public async Task L2_SJOB_06_RetryDoesNotDoublePostPayment()
        {
            await ResetAsync();
            var (productId, _) = await SetInventoryAsync(onHand: 100, reserved: 10);
            var orderId = await SeedOrderAsync(productId, 1, DateTime.UtcNow,
                OrderStatus.Confirmed, PaymentStatus.Paid, PaymentMethod.SePay);

            var orderCode = await QueryAsync(db => db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId).Select(o => o.OrderCode).FirstAsync());

            var logId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                db.PaymentTransactions.Add(new PaymentTransaction
                {
                    OrderId = orderId, TransactionId = "FT-ORIGINAL", Amount = 1_000_000m,
                    AccountNumber = "x", ReferenceCode = orderCode, IsSuccess = true, Timestamp = DateTime.UtcNow
                });
                db.WebhookLogs.Add(new WebhookLog
                {
                    Id = logId,
                    Source = "SePay",
                    RawPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        gateway = "TPBank", transactionDate = "2026-08-06 10:00:00", accountNumber = "x",
                        transferAmount = 1_000_000m, transferType = "in", transferContent = orderCode,
                        content = orderCode, referenceCode = "FT-ORIGINAL", referenceNumber = "FT-ORIGINAL"
                    }),
                    Status = WebhookLogStatus.Failed,
                    AttemptCount = 1,
                    ReceivedAt = DateTime.UtcNow
                });
                await Task.CompletedTask;
            });

            var notifBefore = await QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == orderId));

            await Factory.RunJobAsync<SePayWebhookRetryJob>();

            // (b) DB
            (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                .Should().Be(1, "retry không được ghi nhận tiền lần hai");
            var log = await QueryAsync(db => db.WebhookLogs.AsNoTracking().FirstAsync(w => w.Id == logId));
            log.Status.Should().Be(WebhookLogStatus.Processed);

            // (c) side effect
            (await QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == orderId)))
                .Should().Be(notifBefore, "không thông báo lặp cho Sales");
        }

        // GIVEN  WebhookLog có AttemptCount = max-1 / max / max+1 (WebhookLogService.MaxAttempts)
        // WHEN   Chạy SePayWebhookRetryJob
        // THEN   Tới max vẫn thử lại; vượt max thì dừng, đánh dấu thất bại cuối, cảnh báo Admin
        [Theory]
        [Trait("TestID", "L2-SJOB-07")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-049; FT-09 BV-03")]
        [InlineData(-1, true)]   // còn lượt -> thử lại, và lần thử này chạm trần
        [InlineData(0, false)]   // đã dùng hết lượt -> dừng hẳn
        public async Task L2_SJOB_07_StopsRetryingAfterMaxAttempts(int offsetFromMax, bool expectRetried)
        {
            await ResetAsync();
            var attempts = WebhookLogService.MaxAttempts + offsetFromMax;

            var logId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                db.WebhookLogs.Add(new WebhookLog
                {
                    Id = logId, Source = "SePay",
                    // Payload hỏng -> RetryAsync ném khi deserialize -> MarkFailedAsync tăng AttemptCount.
                    // (Payload hợp lệ nhưng trỏ đơn không tồn tại sẽ được coi là XỬ LÝ THÀNH CÔNG
                    //  vì ProcessSePayWebhookAsync return im lặng, nên không dùng được để test retry.)
                    RawPayload = "{ khong-phai-json",
                    Status = WebhookLogStatus.Failed, AttemptCount = attempts, ReceivedAt = DateTime.UtcNow
                });
                await Task.CompletedTask;
            });

            var processed = await Factory.RunJobAsync<SePayWebhookRetryJob>();

            var log = await QueryAsync(db => db.WebhookLogs.AsNoTracking().FirstAsync(w => w.Id == logId));
            var adminAlerts = await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == logId && n.Type == NotificationType.SYS_26_WebhookRetryExhausted));

            if (expectRetried)
            {
                processed.Should().Be(1, "AttemptCount = max-1 vẫn còn lượt thử");
                log.AttemptCount.Should().Be(WebhookLogService.MaxAttempts, "lần thử này chạm trần");
                log.Status.Should().Be(WebhookLogStatus.Abandoned, "chạm trần phải đánh dấu thất bại cuối");
                adminAlerts.Should().BeGreaterThan(0, "phải cảnh báo Admin khi hết lượt thử");
            }
            else
            {
                processed.Should().Be(0, "đã dùng hết lượt thì job phải dừng, không thử thêm");
                log.AttemptCount.Should().Be(attempts, "không được tăng thêm lượt");
                adminAlerts.Should().Be(0, "không cảnh báo lặp ở các chu kỳ sau");
            }
        }

        // ══════════════════ LowStockAlertJob ══════════════════

        // GIVEN  Tồn khả dụng = ngưỡng+1 / ngưỡng / ngưỡng-1 (Inventory.ReorderThreshold)
        // WHEN   Chạy LowStockAlertJob
        // THEN   ngưỡng+1 không alert; ngưỡng và ngưỡng-1 có đúng 1 alert kèm đủ thông tin
        [Theory]
        [Trait("TestID", "L2-SJOB-08")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-12 AC-04; BV-03")]
        [InlineData(1, false)]
        [InlineData(0, true)]
        [InlineData(-1, true)]
        public async Task L2_SJOB_08_AlertsWhenAtOrBelowReorderThreshold(int offset, bool expectAlert)
        {
            await ResetAsync();
            const int threshold = 20;
            var (_, inventoryId) = await SetInventoryAsync(
                onHand: threshold + offset, reserved: 0, reorderThreshold: threshold);

            await Factory.RunJobAsync<LowStockAlertJob>();

            var alerts = await QueryAsync(db => db.Notifications.AsNoTracking()
                .Where(n => n.ReferenceId == inventoryId && n.Type == NotificationType.SYS_20_LowStockAlert)
                .ToListAsync());

            if (!expectAlert)
            {
                alerts.Should().BeEmpty("tồn {0} vẫn trên ngưỡng {1}", threshold + offset, threshold);
            }
            else
            {
                alerts.Should().NotBeEmpty();
                alerts.Select(a => a.ReferenceType).Should().AllBe("Inventory");
                alerts.Should().OnlyContain(a => a.Body.Contains(threshold.ToString()),
                    "nội dung cảnh báo phải nêu ngưỡng để người nhận biết hành động tiếp theo");
            }
        }

        // GIVEN  P1 đã có alert tồn thấp đang mở
        // WHEN   Chạy LowStockAlertJob thêm 1 chu kỳ
        // THEN   Vẫn đúng số alert cũ (được cập nhật, không nhân bản)
        [Fact]
        [Trait("TestID", "L2-SJOB-09")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-12 AC-04; FT-09 AC-02")]
        public async Task L2_SJOB_09_LowStockAlertIsNotDuplicated()
        {
            await ResetAsync();
            var (_, inventoryId) = await SetInventoryAsync(onHand: 5, reorderThreshold: 20);

            await Factory.RunJobAsync<LowStockAlertJob>();
            var first = await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == inventoryId && n.Type == NotificationType.SYS_20_LowStockAlert));
            first.Should().BeGreaterThan(0);

            await Factory.RunJobAsync<LowStockAlertJob>();
            (await QueryAsync(db => db.Notifications.CountAsync(n =>
                n.ReferenceId == inventoryId && n.Type == NotificationType.SYS_20_LowStockAlert)))
                .Should().Be(first, "cooldown 24h phải chặn cảnh báo trùng ở chu kỳ kế tiếp");
        }

        // ══════════════════ MarketingPostMakeScheduleJob — BLOCKED GH-03b ══════════════════

        // GIVEN  3 bài marketing: nháp tới giờ, đã duyệt chưa tới giờ, đã duyệt tới giờ
        // WHEN   Chạy MarketingPostMakeScheduleJob
        // THEN   Chỉ bài thứ 3 gọi webhook; hai bài còn lại không đổi trạng thái
        [Fact]
        [Trait("TestID", "L2-SJOB-10")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-10; BR-046; BR-049")]
        [Trait("Blocked", "GH-03b")]
        public async Task L2_SJOB_10_OnlyApprovedAndDuePostIsSentToMake()
        {
            AllowOutboundMakeWebhook = true;
            await ResetAsync();

            // GH-03b (bảng MarketingPosts) đã fix qua migration InitData. CreatedByUserId là FK bắt buộc
            // (NOT NULL) tới Users — phải gán đúng user đã seed sẵn, nếu không insert lỗi FK violation.
            await SeedAsync(async db =>
            {
                var productId = await db.Products.Select(p => p.Id).FirstAsync();
                var creatorId = Guid.Parse("44444444-4444-4444-4444-444444444444"); // Sales Staff Test (seed cố định)
                var now = DateTime.UtcNow;
                db.MarketingPosts.AddRange(
                    new MarketingPost { ProductId = productId, CreatedByUserId = creatorId, Status = MarketingPostStatus.Draft, ScheduledTime = now.AddMinutes(-5) },
                    new MarketingPost { ProductId = productId, CreatedByUserId = creatorId, Status = MarketingPostStatus.Approved, ScheduledTime = now.AddHours(2) },
                    new MarketingPost { ProductId = productId, CreatedByUserId = creatorId, Status = MarketingPostStatus.Approved, ScheduledTime = now.AddMinutes(-5) });
            });

            await Factory.RunJobAsync<MarketingPostMakeScheduleJob>();

            Factory.MakeWebhook.Triggered.Should().ContainSingle("chỉ bài đã duyệt VÀ tới giờ được đẩy đi");
        }

        // GIVEN  Bài marketing đã duyệt, tới giờ đăng, Make.com trả lỗi
        // WHEN   Chạy MarketingPostMakeScheduleJob
        // THEN   Bài đánh dấu thất bại và giữ lịch sử; JobRun Failed + retryCount; có cảnh báo
        [Fact]
        [Trait("TestID", "L2-SJOB-11")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BR-049; FT-09 AC-05")]
        [Trait("Blocked", "GH-03b")]
        public async Task L2_SJOB_11_FailedMakeCallIsRecordedAndNotRetriedBlindly()
        {
            AllowOutboundMakeWebhook = true;
            await ResetAsync();
            Factory.MakeWebhook.NextResult = false;   // mô phỏng Make.com lỗi

            Guid postId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var productId = await db.Products.Select(p => p.Id).FirstAsync();
                db.MarketingPosts.Add(new MarketingPost
                {
                    Id = postId, ProductId = productId,
                    CreatedByUserId = Guid.Parse("44444444-4444-4444-4444-444444444444"), // Sales Staff Test (seed cố định)
                    Status = MarketingPostStatus.Approved,
                    ScheduledTime = DateTime.UtcNow.AddMinutes(-5)
                });
            });

            await Factory.RunJobAsync<MarketingPostMakeScheduleJob>();

            var post = await QueryAsync(db => db.MarketingPosts.AsNoTracking().FirstAsync(p => p.Id == postId));
            post.Status.Should().NotBe(MarketingPostStatus.Approved, "thất bại phải được ghi nhận, không im lặng");

            // Chạy lại: không được đẩy mù lần hai khi đã đánh dấu thất bại
            var triggeredAfterFirst = Factory.MakeWebhook.Triggered.Count;
            await Factory.RunJobAsync<MarketingPostMakeScheduleJob>();
            Factory.MakeWebhook.Triggered.Count.Should().Be(triggeredAfterFirst);
        }

        // ══════════════════ QuotationExpiryJob ══════════════════

        // GIVEN  Báo giá Q1 đã duyệt, hạn hiệu lực trước hạn / quá hạn
        // WHEN   Gọi trực tiếp QuotationExpiryJob.RunAsync(ct)
        // THEN   Trước hạn giữ nguyên; quá hạn chuyển Expired và có thông báo cho Sales phụ trách
        [Theory]
        [Trait("TestID", "L2-SJOB-12")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 AC-04; BR-007")]
        [InlineData(5, false)]    // ValidUntil = NOW + 5' -> còn hiệu lực
        [InlineData(-5, true)]    // ValidUntil = NOW - 5' -> hết hiệu lực
        public async Task L2_SJOB_12_ExpiresQuotationPastValidUntil(int validUntilOffsetMinutes, bool expectExpired)
        {
            await ResetAsync();
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var quotationId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Quotations.Add(new Quotation
                {
                    Id = quotationId,
                    CustomerProfileId = profileId,
                    SalesStaffId = sales.Id,
                    Status = QuotationStatus.Approved,
                    OriginalTotal = 120_000_000m,
                    RequestDate = DateTime.UtcNow.AddDays(-1),
                    ValidUntil = DateTime.UtcNow.AddMinutes(validUntilOffsetMinutes)
                });
                await Task.CompletedTask;
            });

            await Factory.RunJobAsync<QuotationExpiryJob>();

            // (b) DB
            var q = await QueryAsync(db => db.Quotations.AsNoTracking().FirstAsync(x => x.Id == quotationId));
            var notifCount = await CountNotifAsync(quotationId, NotificationType.SYS_28_QuotationExpired);

            if (expectExpired)
            {
                q.Status.Should().Be(QuotationStatus.Expired);
                notifCount.Should().BeGreaterThan(0, "phải thông báo cho Sales phụ trách");
            }
            else
            {
                q.Status.Should().Be(QuotationStatus.Approved, "còn hiệu lực thì không được đụng vào");
                notifCount.Should().Be(0);
            }
        }

        // GIVEN  Báo giá Q1 đã bị đánh dấu hết hiệu lực ở chu kỳ trước
        // WHEN   Chạy QuotationExpiryJob thêm 1 chu kỳ
        // THEN   Không đổi trạng thái lần 2; không tạo thông báo trùng
        [Fact]
        [Trait("TestID", "L2-SJOB-13")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-02 AC-04; BR-029")]
        public async Task L2_SJOB_13_QuotationExpiryIsIdempotent()
        {
            await ResetAsync();
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var quotationId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Quotations.Add(new Quotation
                {
                    Id = quotationId, CustomerProfileId = profileId, SalesStaffId = sales.Id,
                    Status = QuotationStatus.Approved, OriginalTotal = 120_000_000m,
                    RequestDate = DateTime.UtcNow.AddDays(-1),
                    ValidUntil = DateTime.UtcNow.AddMinutes(-5)
                });
                await Task.CompletedTask;
            });

            (await Factory.RunJobAsync<QuotationExpiryJob>()).Should().Be(1);
            var notifAfterFirst = await CountNotifAsync(quotationId, NotificationType.SYS_28_QuotationExpired);

            (await Factory.RunJobAsync<QuotationExpiryJob>()).Should().Be(0, "chu kỳ 2 không còn báo giá nào để xử lý");
            (await QueryAsync(db => db.Quotations.AsNoTracking().FirstAsync(x => x.Id == quotationId)))
                .Status.Should().Be(QuotationStatus.Expired);
            (await CountNotifAsync(quotationId, NotificationType.SYS_28_QuotationExpired))
                .Should().Be(notifAfterFirst, "không được tạo thông báo trùng");
        }

        // ══════════════════ UpcomingDeliveryReminderJob ══════════════════

        // GIVEN  3 chuyến giao: 1 vào ngày mai, 1 hôm nay, 1 đã Delivered
        // WHEN   Gọi trực tiếp UpcomingDeliveryReminderJob.RunAsync(ct)
        // THEN   Chỉ chuyến ngày mai được nhắc; chuyến đã giao xong bị bỏ qua
        [Fact]
        [Trait("TestID", "L2-SJOB-14")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-07 AC-01; BR-037")]
        public async Task L2_SJOB_14_RemindsOnlyTomorrowScheduledDeliveries()
        {
            await ResetAsync();
            var (productId, _) = await SetInventoryAsync(onHand: 100);
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);
            await SeedAsync(async db =>
            {
                var profile = await db.CustomerProfiles.FirstAsync();
                profile.AssignedSalesStaffId = sales.Id;
            });

            // Job tính "ngày mai" theo giờ VN: now.AddHours(7).Date.AddDays(1)
            var localTomorrow = DateTime.UtcNow.AddHours(7).Date.AddDays(1);

            var tomorrow = await SeedOrderAsync(productId, 1, DateTime.UtcNow, OrderStatus.Confirmed,
                PaymentStatus.Paid, PaymentMethod.SePay,
                o => { o.DeliveryStatus = DeliveryStatus.Scheduled; o.ScheduledDeliveryDate = localTomorrow; o.DeliveryShift = "Sáng"; });
            var today = await SeedOrderAsync(productId, 1, DateTime.UtcNow, OrderStatus.Confirmed,
                PaymentStatus.Paid, PaymentMethod.SePay,
                o => { o.DeliveryStatus = DeliveryStatus.Scheduled; o.ScheduledDeliveryDate = localTomorrow.AddDays(-1); });
            var delivered = await SeedOrderAsync(productId, 1, DateTime.UtcNow, OrderStatus.Completed,
                PaymentStatus.Paid, PaymentMethod.SePay,
                o => { o.DeliveryStatus = DeliveryStatus.Delivered; o.ScheduledDeliveryDate = localTomorrow; });

            await Factory.RunJobAsync<UpcomingDeliveryReminderJob>();

            var t = NotificationType.SYS_29_UpcomingDeliveryReminder;
            (await CountNotifAsync(tomorrow, t)).Should().BeGreaterThan(0, "chuyến ngày mai phải được nhắc");
            (await CountNotifAsync(today, t)).Should().Be(0, "chuyến hôm nay không thuộc phạm vi nhắc");
            (await CountNotifAsync(delivered, t)).Should().Be(0, "chuyến đã giao xong phải bị bỏ qua");
        }

        // GIVEN  Chuyến giao ngày mai đã được nhắc ở chu kỳ trước
        // WHEN   Chạy UpcomingDeliveryReminderJob thêm 1 chu kỳ
        // THEN   Không gửi nhắc lần 2 cho cùng chuyến
        [Fact]
        [Trait("TestID", "L2-SJOB-15")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-07 AC-01; FT-09 AC-02")]
        public async Task L2_SJOB_15_DeliveryReminderIsNotSentTwice()
        {
            await ResetAsync();
            var (productId, _) = await SetInventoryAsync(onHand: 100);
            var (_, sales) = await CreateClientAsAsync(SystemRole.SalesStaff);
            await SeedAsync(async db =>
            {
                var profile = await db.CustomerProfiles.FirstAsync();
                profile.AssignedSalesStaffId = sales.Id;
            });

            var localTomorrow = DateTime.UtcNow.AddHours(7).Date.AddDays(1);
            var orderId = await SeedOrderAsync(productId, 1, DateTime.UtcNow, OrderStatus.Confirmed,
                PaymentStatus.Paid, PaymentMethod.SePay,
                o => { o.DeliveryStatus = DeliveryStatus.Scheduled; o.ScheduledDeliveryDate = localTomorrow; });

            await Factory.RunJobAsync<UpcomingDeliveryReminderJob>();
            var first = await CountNotifAsync(orderId, NotificationType.SYS_29_UpcomingDeliveryReminder);
            first.Should().BeGreaterThan(0);

            await Factory.RunJobAsync<UpcomingDeliveryReminderJob>();
            (await CountNotifAsync(orderId, NotificationType.SYS_29_UpcomingDeliveryReminder))
                .Should().Be(first, "cooldown phải chặn nhắc lần hai cho cùng chuyến");
        }
    }
}
