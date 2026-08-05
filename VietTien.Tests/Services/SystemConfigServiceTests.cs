using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: SystemConfigService — L1-CFG-01..06. EF InMemory + mock IAuditLogService.
    /// Signature thật: GetEffectiveValueAsync(key, asOf) trả string? (phải parse trước khi so sánh);
    /// SetValueAsync(key, request, actorUserId, actorEmail, ipAddress) — đủ 5 tham số.
    /// </summary>
    public class SystemConfigServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly SystemConfigService _sut;

        private static readonly Guid ActorId = Guid.NewGuid();
        private const string ActorEmail = "admin@viettien.com";
        private const string ActorIp = "10.0.0.5";

        public SystemConfigServiceTests()
        {
            _sut = new SystemConfigService(_db, _audit.Object);
        }

        // ── Block: GetEffectiveValueAsync() ─────────────────────────────────

        // L1-CFG-01 | BVA | Lấy giá trị tại T-1s / T / T+1s quanh thời điểm hiệu lực của phiên bản mới
        [Fact]
        public async Task L1_CFG_01_EffectiveValue_AtBoundaryOfEffectiveDate()
        {
            var t = DateTime.UtcNow.AddHours(1); // thời điểm bản mới bắt đầu hiệu lực
            TestData.SeedConfig(_db, "QUOTATION_MIN_VALUE", "100000000", SystemConfigValueType.Int, DateTime.UtcNow.AddDays(-10));
            TestData.SeedConfig(_db, "QUOTATION_MIN_VALUE", "120000000", SystemConfigValueType.Int, t);

            var before = await _sut.GetEffectiveValueAsync("QUOTATION_MIN_VALUE", t.AddSeconds(-1));
            var at = await _sut.GetEffectiveValueAsync("QUOTATION_MIN_VALUE", t);
            var after = await _sut.GetEffectiveValueAsync("QUOTATION_MIN_VALUE", t.AddSeconds(1));

            before.Should().Be("100000000", "T-1s vẫn dùng giá trị CŨ");
            at.Should().Be("120000000", "đúng mốc T đã áp giá trị MỚI");
            after.Should().Be("120000000");
        }

        // L1-CFG-02 | EP-Invalid | Khoá không tồn tại -> trả null (string?), KHÔNG ném lỗi hạ tầng
        [Fact]
        public async Task L1_CFG_02_UnknownKey_ReturnsNullWithoutThrowing()
        {
            var value = await _sut.GetEffectiveValueAsync("UNKNOWN_KEY");

            value.Should().BeNull("tầng gọi phải tự có giá trị mặc định");
        }

        // ── Block: SetValueAsync() / GetHistoryAsync() ──────────────────────

        // L1-CFG-03 | EP-Valid | Đổi cấu hình tạo PHIÊN BẢN mới + ghi audit, không ghi đè bản cũ
        [Fact]
        public async Task L1_CFG_03_SetValue_CreatesNewVersionAndAudits()
        {
            TestData.SeedConfig(_db, "COD_WARNING_MINUTES", "25");

            await _sut.SetValueAsync("COD_WARNING_MINUTES",
                new UpdateSystemConfigRequest { Value = "20", Reason = "Rút ngắn SLA cảnh báo COD" },
                ActorId, ActorEmail, ActorIp);

            var history = await _sut.GetHistoryAsync("COD_WARNING_MINUTES");
            history.Select(h => h.Value).Should().BeEquivalentTo(new[] { "25", "20" }, "bản cũ vẫn còn nguyên");
            (await _sut.GetEffectiveValueAsync("COD_WARNING_MINUTES")).Should().Be("20");

            _audit.Verify(a => a.LogAsync(
                "SystemConfig", "COD_WARNING_MINUTES", "CONFIG_CHANGE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(),
                It.IsAny<string>(), ActorIp), Times.Once);
        }

        // L1-CFG-04 | EP-Invalid | Đặt thời điểm hiệu lực vào QUÁ KHỨ để tính lại dữ liệu cũ -> từ chối
        // 🔴 SPEC GAP v2.2: SetValueAsync nhận thẳng request.EffectiveDate, KHÔNG chặn ngày quá khứ.
        // Test này ĐỎ cho tới khi bổ sung guard chống thay đổi hồi tố (FT-09 NAC-04; BR-050).
        [Fact]
        public async Task L1_CFG_04_RetroactiveEffectiveDate_IsRejected()
        {
            TestData.SeedConfig(_db, "LIST_PRICE_MAX_EXCLUSIVE", "100000000");

            var act = () => _sut.SetValueAsync("LIST_PRICE_MAX_EXCLUSIVE",
                new UpdateSystemConfigRequest
                {
                    Value = "80000000",
                    EffectiveDate = DateTime.UtcNow.AddDays(-1),
                    Reason = "Thử áp hồi tố"
                },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("không được thay đổi hồi tố làm tính lại giỏ hàng/đơn cũ");
            _db.SystemConfigVersions.Count(v => v.ConfigKey == "LIST_PRICE_MAX_EXCLUSIVE").Should().Be(1);
        }

        // L1-CFG-05 | EP-Invalid | Ghi audit thất bại -> giao dịch cấu hình KHÔNG được coi là hoàn tất
        [Fact]
        public async Task L1_CFG_05_AuditFailure_PropagatesAndDoesNotCompleteQuietly()
        {
            TestData.SeedConfig(_db, "PRICE_LOCK_HOURS", "24");
            _audit.Setup(a => a.LogAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Audit store unavailable"));

            var act = () => _sut.SetValueAsync("PRICE_LOCK_HOURS",
                new UpdateSystemConfigRequest { Value = "48", Reason = "Kéo dài khoá giá" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("lỗi ghi audit phải nổi lên, không được nuốt im lặng");
        }

        // L1-CFG-06 | EP-Valid | Lịch sử trả đủ các bản, sắp theo thời gian, kèm actor
        [Fact]
        public async Task L1_CFG_06_History_IsOrderedAndCarriesActor()
        {
            TestData.SeedConfig(_db, "SEPAY_RESERVATION_MINUTES", "10", SystemConfigValueType.Int, DateTime.UtcNow.AddDays(-3));
            TestData.SeedConfig(_db, "SEPAY_RESERVATION_MINUTES", "12", SystemConfigValueType.Int, DateTime.UtcNow.AddDays(-2));
            await _sut.SetValueAsync("SEPAY_RESERVATION_MINUTES",
                new UpdateSystemConfigRequest { Value = "15", Reason = "Chuẩn hoá theo SRS v2" },
                ActorId, ActorEmail, ActorIp);

            var history = await _sut.GetHistoryAsync("SEPAY_RESERVATION_MINUTES");

            history.Should().HaveCount(3);
            history.Select(h => h.EffectiveDate).Should().BeInDescendingOrder();
            history.First().Value.Should().Be("15");
            history.First().ActorEmail.Should().Be(ActorEmail);
            history.Single(h => h.IsCurrentlyEffective).Value.Should().Be("15");
        }
    }
}
