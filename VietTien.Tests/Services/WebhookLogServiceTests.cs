using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.SePay;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: WebhookLogService — L1-WHL-01..07. EF InMemory + mock IOrderService/INotificationService.
    /// Chốt theo code thật: MaxAttempts = 5 là HẰNG SỐ trong service (không phải SystemConfig key);
    /// trạng thái cuối khi cạn lượt là Abandoned (3); MarkFailedAsync nhận Exception;
    /// LogReceivedAsync trả Guid và chỉ nhận payload; RetryAsync đọc token từ cấu hình SERVER.
    /// </summary>
    public class WebhookLogServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IOrderService> _orderService = new();
        private readonly Mock<INotificationService> _notification = new();
        private readonly WebhookLogService _sut;

        public WebhookLogServiceTests()
        {
            _sut = new WebhookLogService(_db, _orderService.Object, _notification.Object, TestConfig.Create());
        }

        private static SePayWebhookDto Payload(string content = "VT123456", decimal amount = 5_000_000m) => new()
        {
            id = 1,
            gateway = "TPBank",
            transferAmount = amount,
            transferType = "in",
            content = content,
            transferContent = content,
            referenceCode = "FT" + Random.Shared.Next(100000, 999999),
        };

        // ── Block: Ghi nhận & đánh dấu ──────────────────────────────────────

        // L1-WHL-01 | EP-Valid | Mọi callback đều được ghi log TRƯỚC khi validate — kể cả callback chữ ký sai
        [Fact]
        public async Task L1_WHL_01_LogReceived_LogsEveryCallbackIncludingInvalidOnes()
        {
            var id = await _sut.LogReceivedAsync(Payload());

            id.Should().NotBeEmpty();
            var log = _db.WebhookLogs.Single(w => w.Id == id);
            log.Status.Should().Be(WebhookLogStatus.Received);
            log.Source.Should().Be("SePay");
            log.RawPayload.Should().Contain("VT123456");
        }

        // L1-WHL-02 | EP-Valid | Đánh dấu đã xử lý -> Status = Processed (1), lưu mốc thời gian
        [Fact]
        public async Task L1_WHL_02_MarkProcessed_SetsProcessedStatus()
        {
            var log = TestData.WebhookLog();
            _db.WebhookLogs.Add(log);
            _db.SaveChanges();

            await _sut.MarkProcessedAsync(log.Id);

            var updated = _db.WebhookLogs.Single(w => w.Id == log.Id);
            updated.Status.Should().Be(WebhookLogStatus.Processed);
            updated.ProcessedAt.Should().NotBeNull();
        }

        // L1-WHL-03 | EP-Valid | Đánh dấu thất bại -> AttemptCount tăng, giữ nội dung lỗi, Status = Failed (2)
        [Fact]
        public async Task L1_WHL_03_MarkFailed_IncrementsAttemptAndKeepsError()
        {
            var log = TestData.WebhookLog(w => { w.Status = WebhookLogStatus.Failed; w.AttemptCount = 1; });
            _db.WebhookLogs.Add(log);
            _db.SaveChanges();

            await _sut.MarkFailedAsync(log.Id, new InvalidOperationException("Không tìm thấy đơn hàng khớp nội dung."));

            var updated = _db.WebhookLogs.Single(w => w.Id == log.Id);
            updated.AttemptCount.Should().Be(2);
            updated.Status.Should().Be(WebhookLogStatus.Failed);
            updated.LastError.Should().Contain("Không tìm thấy đơn hàng");
        }

        // L1-WHL-04 | EP-Valid | Search lọc theo trạng thái, trả PagedResultDto đúng số bản ghi
        [Fact]
        public async Task L1_WHL_04_Search_FiltersByStatus()
        {
            _db.WebhookLogs.AddRange(
                TestData.WebhookLog(w => w.Status = WebhookLogStatus.Processed),
                TestData.WebhookLog(w => w.Status = WebhookLogStatus.Failed),
                TestData.WebhookLog(w => w.Status = WebhookLogStatus.Failed));
            _db.SaveChanges();

            var result = await _sut.SearchAsync(new WebhookLogQueryDto { Status = "Failed" });

            result.TotalCount.Should().Be(2);
            result.Items.Should().OnlyContain(i => i.Status == "Failed");
        }

        // ── Block: RetryAsync() ─────────────────────────────────────────────

        // L1-WHL-05 | EP-Valid | Retry dùng token ĐỌC TỪ CẤU HÌNH SERVER, không dùng token trong payload cũ
        [Fact]
        public async Task L1_WHL_05_Retry_UsesTrustedServerToken()
        {
            var id = await _sut.LogReceivedAsync(Payload());
            _db.WebhookLogs.Single(w => w.Id == id).Status = WebhookLogStatus.Failed;
            _db.SaveChanges();

            var dto = await _sut.RetryAsync(id);

            _orderService.Verify(o => o.ProcessSePayWebhookAsync(
                It.Is<SePayWebhookDto>(p => p.content == "VT123456"),
                TestConfig.SePayApiToken), Times.Once);
            dto.Status.Should().Be(nameof(WebhookLogStatus.Processed));
        }

        // L1-WHL-06 | EP-Valid | Retry log đã Processed -> ỦY QUYỀN xuống đúng 1 lần, giữ nguyên trạng thái.
        //
        // ⚠ Case này KHÔNG chứng minh tính idempotent (rà soát 01/08/2026 — đã bỏ nhãn "Idempotency"
        //   khỏi tên vì gây hiểu nhầm). WebhookLogService chỉ điều phối; guard thật "không tạo
        //   PaymentTransaction thứ 2" nằm trong OrderService.ProcessSePayWebhookAsync và được phủ bởi
        //   L1-ORD-18. Ở đây chỉ khẳng định lớp điều phối không tự nhân đôi lời gọi.
        [Fact]
        public async Task L1_WHL_06_Retry_AlreadyProcessed_DelegatesOnceAndKeepsStatus()
        {
            var id = await _sut.LogReceivedAsync(Payload());
            var log = _db.WebhookLogs.Single(w => w.Id == id);
            log.Status = WebhookLogStatus.Processed;
            log.ProcessedAt = DateTime.UtcNow;
            _db.SaveChanges();

            var dto = await _sut.RetryAsync(id);

            _orderService.Verify(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), It.IsAny<string>()), Times.Once);
            dto.Status.Should().Be(nameof(WebhookLogStatus.Processed));
            _db.WebhookLogs.Should().ContainSingle("không sinh thêm bản ghi webhook nào");
        }

        // L1-WHL-07 | BVA-Max+1 | Quanh ngưỡng MaxAttempts = 5: dưới ngưỡng vẫn Failed (còn thử lại),
        // chạm ngưỡng -> Abandoned (3), dừng vĩnh viễn và tạo cảnh báo cho Admin.
        [Theory]
        [InlineData(3, WebhookLogStatus.Failed, false)]     // 3 -> 4, còn dưới 5
        [InlineData(4, WebhookLogStatus.Abandoned, true)]   // 4 -> 5, chạm MaxAttempts
        [InlineData(5, WebhookLogStatus.Abandoned, true)]   // 5 -> 6, đã vượt
        public async Task L1_WHL_07_Retry_StopsAtMaxAttempts(int attemptsBefore, WebhookLogStatus expectedStatus, bool expectAlert)
        {
            WebhookLogService.MaxAttempts.Should().Be(5, "MaxAttempts là hằng số hard-code trong service");

            var id = await _sut.LogReceivedAsync(Payload());
            var log = _db.WebhookLogs.Single(w => w.Id == id);
            log.Status = WebhookLogStatus.Failed;
            log.AttemptCount = attemptsBefore;
            _db.SaveChanges();

            _orderService
                .Setup(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("SePay tạm thời không phản hồi."));

            var dto = await _sut.RetryAsync(id);

            dto.Status.Should().Be(expectedStatus.ToString());
            dto.AttemptCount.Should().Be(attemptsBefore + 1);
            _notification.Verify(n => n.CreateRoleNotificationAsync(
                    NotificationType.SYS_26_WebhookRetryExhausted, SystemRole.Admin,
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()),
                expectAlert ? Times.Once() : Times.Never());
        }
    }
}
