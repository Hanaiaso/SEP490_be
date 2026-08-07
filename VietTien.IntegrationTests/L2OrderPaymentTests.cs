using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.DTOs.Payment;
using VietTien.API.DTOs.SePay;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.API.Services.ScheduledJobs;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-OrderPayment — batch 1.
    ///
    /// Mỗi case assert ĐỦ 3 TẦNG (khác L1):
    ///   (a) HTTP status + body
    ///   (b) trạng thái DB — luôn query lại bằng scope MỚI qua QueryAsync(), không tin response
    ///       và không đọc entity đã tracked
    ///   (c) side effect — Notification (INotificationService ghi xuống bảng Notifications) và
    ///       các recording fake của SqlServerFixture
    ///
    /// L2-PAY-05/06 KHÔNG nằm trong file này — xem báo cáo: SePayReservationExpiryJob.cs:34 gọi thẳng
    /// DateTime.UtcNow, chưa có abstraction thời gian để mock.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2OrderPaymentTests : SqlServerTestBase
    {
        public L2OrderPaymentTests(SqlServerFixture factory) : base(factory) { }

        private const string WebhookUrl = "/api/webhooks/sepay-callback";

        private string SePayToken =>
            Factory.Services.GetRequiredService<IConfiguration>()["SePaySettings:ApiToken"]!;

        // ── Helpers ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Dựng 1 đơn SePay đang chờ thanh toán, dùng lại CustomerProfile/Product/Inventory từ seed
        /// HasData (ResetAsync đã nạp lại). Inventory được set Reserved đủ lớn để mô phỏng trạng thái
        /// sau checkout — nhờ đó nhánh allocation (Release rồi Allocate) chạy được tất định.
        /// OrderCode phải khớp regex ExtractOrderCode (OrderService.cs:753): VT + chữ số.
        /// </summary>
        private async Task<(Guid OrderId, string OrderCode, decimal FinalPayment)> SeedSePayOrderAsync(
            decimal finalPayment, OrderStatus status = OrderStatus.Draft)
        {
            var orderId = Guid.NewGuid();
            var orderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999);

            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                var inventory = await db.Inventories.FirstAsync(i => i.ProductId != null);

                inventory.OnHandQuantity = 10_000;
                inventory.ReservedQuantity = 1_000;
                inventory.AllocatedQuantity = 0;

                db.Orders.Add(new Order
                {
                    Id = orderId,
                    CustomerProfileId = profileId,
                    OrderCode = orderCode,
                    TotalAmount = finalPayment,
                    FinalPayment = finalPayment,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Pending,
                    OrderStatus = status,
                    CreatedAt = DateTime.UtcNow,
                    OrderItems = new List<OrderItem>
                    {
                        new()
                        {
                            ProductId = inventory.ProductId!.Value,
                            Quantity = 1,
                            PriceSnapshot = finalPayment,
                            CostSnapshot = 0m
                        }
                    }
                });
            });

            return (orderId, orderCode, finalPayment);
        }

        private static SePayWebhookDto BuildPayload(string orderCode, decimal amount, string refCode) => new()
        {
            gateway = "TPBank",
            transactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            accountNumber = "71111810204",
            transferAmount = amount,
            transferType = "in",
            transferContent = orderCode,
            content = orderCode,
            referenceCode = refCode,
            referenceNumber = refCode
        };

        private HttpClient WebhookClient(string? token)
        {
            var client = Factory.CreateClient();
            if (token is not null) client.DefaultRequestHeaders.Add("x-sepay-token", token);
            return client;
        }

        private Task<int> CountTransactionsAsync(Guid orderId) =>
            QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId));

        private Task<int> CountNotificationsAsync(Guid orderId) =>
            QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == orderId));

        private Task<Order> ReloadOrderAsync(Guid orderId) =>
            QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));

        // ── L2-PAY-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay ở trạng thái Draft/Pending, FinalPayment = 1.500.000đ, tồn kho đủ
        // WHEN   SePay gọi webhook với token đúng và transferAmount khớp chính xác FinalPayment
        // THEN   200; đơn chuyển Paid + Confirmed; đúng 1 PaymentTransaction mang mã đối soát của SePay
        [Fact]
        [Trait("TestID", "L2-PAY-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "WF-06 / BV-01")]
        public async Task L2_PAY_01_ValidTokenAndExactAmount_MarksOrderPaidAndRecordsTransaction()
        {
            await ResetAsync();
            var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_500_000m);
            var refCode = "FT" + Guid.NewGuid().ToString("N")[..12];

            var response = await WebhookClient(SePayToken)
                .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount, refCode));

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"success\":true");

            // (b) DB — scope mới
            var order = await ReloadOrderAsync(orderId);
            order.PaymentStatus.Should().Be(PaymentStatus.Paid);
            order.OrderStatus.Should().Be(OrderStatus.Confirmed);

            var transactions = await QueryAsync(db => db.PaymentTransactions
                .AsNoTracking().Where(t => t.OrderId == orderId).ToListAsync());
            transactions.Should().ContainSingle();
            transactions[0].TransactionId.Should().Be(refCode);
            transactions[0].Amount.Should().Be(amount);
            transactions[0].IsSuccess.Should().BeTrue();
            transactions[0].Source.Should().Be(PaymentTransactionSource.SePayWebhook);

            // (c) side effect — không có bất thường thanh toán nào được mở
            (await QueryAsync(db => db.PaymentExceptions.CountAsync(p => p.OrderId == orderId)))
                .Should().Be(0);
            (await QueryAsync(db => db.Notifications
                .CountAsync(n => n.ReferenceId == orderId && n.Type == NotificationType.SYS_32_PaymentAnomaly)))
                .Should().Be(0);
        }

        // ── L2-PAY-02 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay đang Pending và một webhook mang chữ ký/token SAI (không rỗng)
        // WHEN   POST /api/webhooks/sepay-callback với x-sepay-token sai
        // THEN   401; đơn giữ nguyên Pending/Draft; KHÔNG tạo PaymentTransaction nào
        [Fact]
        [Trait("TestID", "L2-PAY-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "SEC-03 / WF-06")]
        public async Task L2_PAY_02_InvalidToken_Returns401AndLeavesDatabaseUnchanged()
        {
            await ResetAsync();
            var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_500_000m);

            // Token SAI (KHÔNG rỗng) — nhánh bypass ở OrderService.cs:303 chỉ kích hoạt khi token RỖNG,
            // nên case này tất định trên mọi máy, không phụ thuộc ASPNETCORE_ENVIRONMENT.
            var response = await WebhookClient("sai-token-hoan-toan")
                .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount, "FT-INVALID"));

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"success\":false");

            // (b) DB
            var order = await ReloadOrderAsync(orderId);
            order.PaymentStatus.Should().Be(PaymentStatus.Pending);
            order.OrderStatus.Should().Be(OrderStatus.Draft);
            (await CountTransactionsAsync(orderId)).Should().Be(0);

            // (c) side effect — không notification, không PaymentException
            (await CountNotificationsAsync(orderId)).Should().Be(0);
            (await QueryAsync(db => db.PaymentExceptions.CountAsync(p => p.OrderId == orderId)))
                .Should().Be(0);
        }

        // ── L2-PAY-03 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay FinalPayment = 1.000.000đ
        // WHEN   webhook báo số tiền lệch 1đ (thiếu), khớp chính xác, hoặc lệch 1đ (thừa)
        // THEN   SRS BV-01: chỉ khớp CHÍNH XÁC mới được Paid; thiếu/thừa đều giữ Pending,
        //        mở PaymentException và bắn cảnh báo — KHÔNG return im lặng
        [Theory]
        [Trait("TestID", "L2-PAY-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "BV-01")]
        [InlineData(-1, false, "UNDERPAYMENT")]
        [InlineData(0, true, null)]
        [InlineData(+1, false, "OVERPAYMENT")]
        public async Task L2_PAY_03_AmountMustMatchFinalPaymentExactly(
            int delta, bool expectPaid, string? expectedReasonCode)
        {
            await ResetAsync();
            var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_000_000m);
            var refCode = "FT" + Guid.NewGuid().ToString("N")[..12];

            var response = await WebhookClient(SePayToken)
                .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount + delta, refCode));

            // (a) HTTP — webhook luôn được nhận (200), khác biệt nằm ở hệ quả nghiệp vụ
            response.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            var order = await ReloadOrderAsync(orderId);
            var exceptions = await QueryAsync(db => db.PaymentExceptions
                .AsNoTracking().Where(p => p.OrderId == orderId).ToListAsync());

            if (expectPaid)
            {
                order.PaymentStatus.Should().Be(PaymentStatus.Paid);
                (await CountTransactionsAsync(orderId)).Should().Be(1);
                exceptions.Should().BeEmpty();

                // (c) không có cảnh báo bất thường
                (await QueryAsync(db => db.Notifications.CountAsync(n =>
                    n.ReferenceId == orderId && n.Type == NotificationType.SYS_32_PaymentAnomaly)))
                    .Should().Be(0);
            }
            else
            {
                order.PaymentStatus.Should().Be(PaymentStatus.Pending,
                    "SRS BV-01: lệch dù chỉ 1đ cũng không được tự động ghi Paid");
                order.OrderStatus.Should().Be(OrderStatus.Draft);
                (await CountTransactionsAsync(orderId)).Should().Be(0,
                    "tiền không khớp thì không được ghi nhận giao dịch thành công");

                exceptions.Should().ContainSingle();
                exceptions[0].ReasonCode.Should().Be(expectedReasonCode);
                exceptions[0].Status.Should().Be("OPEN");

                // (c) side effect — phải để lại dấu vết cho người đối soát, không im lặng
                (await QueryAsync(db => db.Notifications.CountAsync(n =>
                    n.ReferenceId == orderId && n.Type == NotificationType.SYS_32_PaymentAnomaly)))
                    .Should().BeGreaterThan(0, "SRS BV-01 cấm return im lặng khi tiền đã vào tài khoản");
            }
        }

        // ── L2-PAY-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  webhook đã xử lý thành công 1 lần cho đơn SePay
        // WHEN   SePay gửi lại NGUYÊN payload đó lần thứ hai (redelivery)
        // THEN   200; vẫn đúng 1 PaymentTransaction; không phát sinh notification thứ 2
        [Fact]
        [Trait("TestID", "L2-PAY-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "WF-06 idempotency")]
        public async Task L2_PAY_04_ReplayingSamePayload_IsIdempotent()
        {
            await ResetAsync();
            var (orderId, orderCode, amount) = await SeedSePayOrderAsync(2_000_000m);
            var refCode = "FT" + Guid.NewGuid().ToString("N")[..12];
            var payload = BuildPayload(orderCode, amount, refCode);

            var first = await WebhookClient(SePayToken).PostAsJsonAsync(WebhookUrl, payload);
            first.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await first.Content.ReadAsStringAsync());

            var txAfterFirst = await CountTransactionsAsync(orderId);
            var notifyAfterFirst = await CountNotificationsAsync(orderId);
            txAfterFirst.Should().Be(1);

            // Gửi lại NGUYÊN payload
            var second = await WebhookClient(SePayToken).PostAsJsonAsync(WebhookUrl, payload);

            // (a) HTTP — lần 2 vẫn được nhận, không lỗi
            second.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await second.Content.ReadAsStringAsync());

            // (b) DB — không nhân đôi giao dịch
            (await CountTransactionsAsync(orderId)).Should().Be(1, "redelivery không được tạo PaymentTransaction thứ 2");
            var order = await ReloadOrderAsync(orderId);
            order.PaymentStatus.Should().Be(PaymentStatus.Paid);

            // (c) side effect — không notification kép
            (await CountNotificationsAsync(orderId)).Should().Be(notifyAfterFirst,
                "lần gửi lại không được sinh thêm thông báo");
        }

        // ── L2-PAY-07 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay đang Pending và một user KHÔNG phải Sales Manager
        // WHEN   POST /api/orders/{orderId}/manual-confirm
        // THEN   403; đơn không đổi; không tạo PaymentTransaction. Sales Manager thì qua được cổng quyền
        [Fact]
        [Trait("TestID", "L2-PAY-07")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "MGR-05")]
        public async Task L2_PAY_07_ManualConfirm_OnlySalesManagerIsAllowed()
        {
            await ResetAsync();
            var (orderId, _, amount) = await SeedSePayOrderAsync(3_000_000m);

            var request = new ManualConfirmPaymentRequest
            {
                ExternalTransactionId = "MC" + Guid.NewGuid().ToString("N")[..12],
                ActualAmount = amount,
                EvidenceUrl = "https://fake.local/evidence/proof.jpg",
                TransferContent = "chuyen khoan doi soat",
                Note = "L2-PAY-07"
            };

            // Không phải Sales Manager -> chặn ở cổng quyền
            foreach (var role in new[] { SystemRole.SalesStaff, SystemRole.AccountingStaff, SystemRole.Customer })
            {
                var (client, _) = await CreateClientAsAsync(role);
                var denied = await client.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm", request);

                // (a) HTTP
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "role {0} không được xác nhận thủ công", role);

                // (b) DB — không đổi gì
                (await ReloadOrderAsync(orderId)).PaymentStatus.Should().Be(PaymentStatus.Pending);
                (await CountTransactionsAsync(orderId)).Should().Be(0);
            }

            // Sales Manager -> qua cổng quyền và xác nhận thành công
            var (managerClient, manager) = await CreateClientAsAsync(SystemRole.SalesManager);
            var allowed = await managerClient.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm", request);

            // (a) HTTP
            allowed.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await allowed.Content.ReadAsStringAsync());

            // (b) DB
            var order = await ReloadOrderAsync(orderId);
            order.PaymentStatus.Should().Be(PaymentStatus.Paid);
            order.ManualConfirmedByUserId.Should().Be(manager.Id);
            order.ManualConfirmEvidenceUrl.Should().Be(request.EvidenceUrl);

            var tx = await QueryAsync(db => db.PaymentTransactions
                .AsNoTracking().Where(t => t.OrderId == orderId).ToListAsync());
            tx.Should().ContainSingle();
            tx[0].Source.Should().Be(PaymentTransactionSource.ManualConfirmation);
            tx[0].IsManualConfirmed.Should().BeTrue();
            tx[0].ConfirmedByUserId.Should().Be(manager.Id);
            tx[0].EvidenceUrl.Should().Be(request.EvidenceUrl);
        }

        // ── L2-PAY-08 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay đang Pending, người gọi là Sales Manager hợp lệ
        // WHEN   manual-confirm nhưng KHÔNG đính kèm bằng chứng đối soát (EvidenceUrl rỗng)
        // THEN   400; đơn giữ nguyên Pending; không tạo PaymentTransaction
        [Theory]
        [Trait("TestID", "L2-PAY-08")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "MGR-05 MANUAL_CONFIRM_EVIDENCE_REQUIRED")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task L2_PAY_08_ManualConfirm_RequiresEvidence(string evidenceUrl)
        {
            await ResetAsync();
            var (orderId, _, amount) = await SeedSePayOrderAsync(3_000_000m);

            var (client, _) = await CreateClientAsAsync(SystemRole.SalesManager);
            var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm",
                new ManualConfirmPaymentRequest
                {
                    ExternalTransactionId = "MC" + Guid.NewGuid().ToString("N")[..12],
                    ActualAmount = amount,
                    EvidenceUrl = evidenceUrl,
                    Note = "L2-PAY-08"
                });

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "bằng chứng đối soát là bắt buộc; body: {0}", await response.Content.ReadAsStringAsync());

            // (b) DB
            var order = await ReloadOrderAsync(orderId);
            order.PaymentStatus.Should().Be(PaymentStatus.Pending);
            order.ManualConfirmedByUserId.Should().BeNull();
            order.ManualConfirmEvidenceUrl.Should().BeNull();
            (await CountTransactionsAsync(orderId)).Should().Be(0);

            // (c) side effect
            (await CountNotificationsAsync(orderId)).Should().Be(0);
        }

        // ── L2-PAY-09 ──────────────────────────────────────────────────────────────────────

        // GIVEN  đơn SePay đang Pending, tồn kho đủ
        // WHEN   webhook SePay và manual-confirm của Sales Manager bắn SONG SONG (cùng chạm một barrier)
        // THEN   đúng 1 bên thắng; đúng 1 PaymentTransaction; không notification kép — lặp 5 lần
        [Fact]
        [Trait("TestID", "L2-PAY-09")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "WF-06 / MGR-05 concurrency")]
        public async Task L2_PAY_09_WebhookAndManualConfirmInParallel_ExactlyOneWins()
        {
            await ResetAsync();
            var (managerClient, _) = await CreateClientAsAsync(SystemRole.SalesManager);

            for (var iteration = 1; iteration <= 5; iteration++)
            {
                var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_234_000m);
                var webhookRef = "FT" + Guid.NewGuid().ToString("N")[..12];
                var manualRef = "MC" + Guid.NewGuid().ToString("N")[..12];

                // Barrier: cả hai request cùng chờ một TaskCompletionSource rồi mới bắn,
                // để chúng thực sự chạm nhau chứ không chạy nối đuôi.
                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var webhookTask = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await WebhookClient(SePayToken)
                        .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount, webhookRef));
                });

                var manualTask = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await managerClient.PostAsJsonAsync($"/api/orders/{orderId}/manual-confirm",
                        new ManualConfirmPaymentRequest
                        {
                            ExternalTransactionId = manualRef,
                            ActualAmount = amount,
                            EvidenceUrl = "https://fake.local/evidence/race.jpg",
                            Note = $"L2-PAY-09 iteration {iteration}"
                        });
                });

                barrier.SetResult();
                await Task.WhenAll(webhookTask, manualTask);

                // (b) DB — bất biến quan trọng nhất: tiền chỉ được ghi nhận MỘT lần
                var transactions = await QueryAsync(db => db.PaymentTransactions
                    .AsNoTracking().Where(t => t.OrderId == orderId).ToListAsync());

                transactions.Should().ContainSingle(
                    "vòng {0}: webhook và manual-confirm chạy song song chỉ được tạo đúng 1 PaymentTransaction, " +
                    "nhưng có {1} (các mã: {2})",
                    iteration, transactions.Count, string.Join(", ", transactions.Select(t => t.TransactionId)));

                var order = await ReloadOrderAsync(orderId);
                order.PaymentStatus.Should().Be(PaymentStatus.Paid, "vòng {0}: đúng một bên phải thắng", iteration);

                // (c) side effect — không có cảnh báo bất thường do ghi đè lẫn nhau
                (await QueryAsync(db => db.Notifications.CountAsync(n =>
                    n.ReferenceId == orderId && n.Type == NotificationType.SYS_32_PaymentAnomaly)))
                    .Should().Be(0, "vòng {0}: không được sinh cảnh báo kép", iteration);
            }
        }

        // ══════════════════ Payment timeout — SePayReservationExpiryJob ══════════════════

        /// <summary>Khả dụng THÔ — chưa kẹp Math.Max, để phát hiện oversell mà AvailableQuantity che mất (R4).</summary>
        private static int RawAvailable(Inventory i) =>
            i.OnHandQuantity - i.ReservedQuantity - i.AllocatedQuantity - i.DamagedQuantity - i.QuarantineQuantity;

        /// <summary>Ngưỡng giữ tồn đọc từ SystemConfig đã seed — R6 cấm hard-code 15.</summary>
        private async Task<int> GetReservationMinutesAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
            var raw = await cfg.GetEffectiveValueAsync("SEPAY_RESERVATION_MINUTES");
            return int.TryParse(raw, out var m) && m > 0 ? m : 15;
        }

        /// <summary>Đặt dòng tồn về trạng thái xác định, các dòng khác của cùng SP về 0.</summary>
        private async Task<(Guid ProductId, Guid InventoryId)> SetInventoryAsync(int onHand, int reserved)
        {
            Guid pid = Guid.Empty, iid = Guid.Empty;
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = onHand; inv.ReservedQuantity = reserved;
                inv.AllocatedQuantity = 0; inv.DamagedQuantity = 0;
                inv.QuarantineQuantity = 0; inv.InTransitQuantity = 0;
                pid = inv.ProductId!.Value; iid = inv.Id;
                foreach (var o in await db.Inventories.Where(i => i.ProductId == pid && i.Id != inv.Id).ToListAsync())
                {
                    o.OnHandQuantity = 0; o.ReservedQuantity = 0; o.AllocatedQuantity = 0;
                }
            });
            return (pid, iid);
        }

        private async Task<Guid> SeedPendingSePayOrderAsync(Guid productId, int qty, DateTime createdAt)
        {
            var id = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Orders.Add(new Order
                {
                    Id = id,
                    CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 1_000_000m, FinalPayment = 1_000_000m,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Pending,
                    OrderStatus = OrderStatus.Draft,
                    CreatedAt = createdAt,
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = productId, Quantity = qty, PriceSnapshot = 1_000_000m, CostSnapshot = 0m }
                    }
                });
            });
            return id;
        }

        // GIVEN  Đơn A backdate quá hạn rõ ràng, đơn B vừa tạo; SEPAY_RESERVATION_MINUTES đọc từ SystemConfig
        // WHEN   Factory.RunJobAsync<SePayReservationExpiryJob>()
        // THEN   A được giải phóng, OnHand không đổi, RawAvailable tăng đúng phần trả lại; B không bị đụng
        [Fact]
        [Trait("TestID", "L2-PAY-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 NAC-03; BV-01; BR-008; BR-033")]
        public async Task L2_PAY_05_ExpiredReservationIsReleasedAndFreshOneIsUntouched()
        {
            await ResetAsync();
            var minutes = await GetReservationMinutesAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 20, reserved: 7);

            // R6: cách ngưỡng 5 phút mỗi bên, không test mốc chính xác.
            var orderA = await SeedPendingSePayOrderAsync(productId, 4, DateTime.UtcNow.AddMinutes(-(minutes + 5)));
            var orderB = await SeedPendingSePayOrderAsync(productId, 3, DateTime.UtcNow);

            var before = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == inventoryId));

            var processed = await Factory.RunJobAsync<SePayReservationExpiryJob>();

            // (a) kết quả job
            processed.Should().Be(1, "chỉ đơn A quá hạn");

            // (b) DB — scope mới
            var a = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderA));
            var b = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderB));
            a.OrderStatus.Should().Be(OrderStatus.Cancelled);
            b.OrderStatus.Should().Be(OrderStatus.Draft, "đơn vừa tạo không được đụng tới");
            b.PaymentStatus.Should().Be(PaymentStatus.Pending);

            var after = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == inventoryId));
            after.OnHandQuantity.Should().Be(before.OnHandQuantity, "giải phóng giữ tồn không đụng tồn vật lý");
            after.ReservedQuantity.Should().Be(3, "chỉ trả lại 4 đơn vị của đơn A, giữ nguyên 3 của đơn B");
            RawAvailable(after).Should().Be(RawAvailable(before) + 4, "khả dụng THÔ tăng đúng phần trả lại");
            after.OnHandQuantity.Should().BeGreaterOrEqualTo(0);
            RawAvailable(after).Should().BeGreaterOrEqualTo(0, "không đơn nào được làm tồn âm");

            // (c) side effect
            a.CancelReason.Should().Contain("SePay");
        }

        // GIVEN  Đơn SePay chờ thanh toán còn trong hạn giữ tồn; Reserved = 3
        // WHEN   Factory.RunJobAsync<SePayReservationExpiryJob>()
        // THEN   Reservation vẫn còn nguyên; đơn giữ nguyên trạng thái chờ thanh toán
        [Fact]
        [Trait("TestID", "L2-PAY-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-03 BV-01; BR-008")]
        public async Task L2_PAY_06_ReservationWithinWindowIsKept()
        {
            await ResetAsync();
            var minutes = await GetReservationMinutesAsync();
            var (productId, inventoryId) = await SetInventoryAsync(onHand: 20, reserved: 3);

            var orderId = await SeedPendingSePayOrderAsync(productId, 3, DateTime.UtcNow.AddMinutes(-(minutes - 5)));

            var processed = await Factory.RunJobAsync<SePayReservationExpiryJob>();

            // (a)
            processed.Should().Be(0, "chưa đơn nào quá hạn");

            // (b) DB
            var order = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
            order.OrderStatus.Should().Be(OrderStatus.Draft);
            order.PaymentStatus.Should().Be(PaymentStatus.Pending);
            order.CancelReason.Should().BeNull();

            var inv = await QueryAsync(db => db.Inventories.AsNoTracking().FirstAsync(i => i.Id == inventoryId));
            inv.ReservedQuantity.Should().Be(3, "còn trong hạn thì không được trả tồn");
            RawAvailable(inv).Should().Be(17);
            RawAvailable(inv).Should().BeGreaterOrEqualTo(0);

            // (c) side effect — không cảnh báo, không huỷ
            (await CountNotificationsAsync(orderId)).Should().Be(0);
        }
    }
}
