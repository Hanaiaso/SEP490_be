using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: DiscountTierService — L1-DT-01..09. EF InMemory + mock IAuditLogService.
    /// Lưu ý signature thật: CreateAsync/UpdateAsync cần bộ 3 tham số audit (actorUserId, actorEmail, ipAddress).
    /// DiscountPercent lưu dạng PHÂN SỐ 0..1 (0.05 = 5%), không phải 0..100.
    /// </summary>
    public class DiscountTierServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly DiscountTierService _sut;

        private static readonly Guid ActorId = Guid.NewGuid();
        private const string ActorEmail = "admin@viettien.com";
        private const string ActorIp = "127.0.0.1";

        public DiscountTierServiceTests()
        {
            _sut = new DiscountTierService(_db, _audit.Object);
        }

        /// <summary>Seed bảng bậc chiết khấu chuẩn: 10tr–50tr = 3%, 50tr–100tr = 5%. Trên 100tr phải đi báo giá.</summary>
        private void SeedStandardTiers()
        {
            _db.DiscountTiers.AddRange(
                TestData.DiscountTier(10_000_000m, 50_000_000m, 0.03m),
                TestData.DiscountTier(50_000_000m, 100_000_000m, 0.05m));
            _db.SaveChanges();
        }

        // ── Block: GetApplicableDiscountPercentAsync() ───────────────────────

        // L1-DT-01 | BVA-Max | Tổng 9.999.999đ -> 0% (dưới ngưỡng bậc đầu tiên, giữ giá niêm yết)
        [Fact]
        public async Task L1_DT_01_BelowFirstTier_ReturnsZero()
        {
            SeedStandardTiers();

            var percent = await _sut.GetApplicableDiscountPercentAsync(9_999_999m);

            percent.Should().Be(0m);
        }

        // L1-DT-02 | BVA-Min | Tổng đúng 10.000.000đ -> áp bậc đầu tiên (biên dưới là INCLUSIVE)
        [Fact]
        public async Task L1_DT_02_ExactlyFirstTierMin_AppliesFirstTier()
        {
            SeedStandardTiers();

            var percent = await _sut.GetApplicableDiscountPercentAsync(10_000_000m);

            percent.Should().Be(0.03m).And.NotBe(0m);
        }

        // L1-DT-03 | BVA-Max | Tổng 99.999.999đ -> vẫn trong dải chiết khấu tự động (bậc cao nhất < 100M)
        [Fact]
        public async Task L1_DT_03_JustBelowQuotationThreshold_AppliesHighestTier()
        {
            SeedStandardTiers();

            var percent = await _sut.GetApplicableDiscountPercentAsync(99_999_999m);

            percent.Should().Be(0.05m);
        }

        // L1-DT-04 | BVA-Min | Tổng đúng 100.000.000đ -> KHÔNG áp chiết khấu tự động (phải qua báo giá)
        [Fact]
        public async Task L1_DT_04_AtQuotationThreshold_NoAutoDiscount()
        {
            SeedStandardTiers();

            var percent = await _sut.GetApplicableDiscountPercentAsync(100_000_000m);

            percent.Should().Be(0m, "từ 100 triệu trở lên phải đi luồng báo giá, không tự áp bậc");
        }

        // L1-DT-05 | BVA-Min-1 | Tổng âm hoặc 0 -> 0%, không ném lỗi hạ tầng
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public async Task L1_DT_05_NonPositiveSubtotal_ReturnsZeroWithoutThrowing(int subtotal)
        {
            SeedStandardTiers();

            var percent = await _sut.GetApplicableDiscountPercentAsync(subtotal);

            percent.Should().Be(0m);
        }

        // L1-DT-06 | EP-Valid | Chỉ dùng bậc đang hiệu lực (IsActive) tại thời điểm tính
        [Fact]
        public async Task L1_DT_06_OnlyActiveTiersAreUsed()
        {
            _db.DiscountTiers.AddRange(
                TestData.DiscountTier(10_000_000m, 50_000_000m, 0.10m, t => t.IsActive = false), // bậc cũ đã ngừng
                TestData.DiscountTier(10_000_000m, 50_000_000m, 0.03m));                          // bậc mới đang hiệu lực
            _db.SaveChanges();

            var percent = await _sut.GetApplicableDiscountPercentAsync(15_000_000m);

            percent.Should().Be(0.03m, "bậc đã ngừng hiệu lực phải bị bỏ qua");
        }

        // ── Block: CRUD ─────────────────────────────────────────────────────

        // L1-DT-07 | EP-Invalid | Tạo bậc CHỒNG LẤN khoảng giá trị -> phải từ chối
        // 🔴 SPEC GAP v2.2: DiscountTierService.Validate() chỉ kiểm tra Min >= 0, Max > Min và Percent 0..1 —
        // KHÔNG kiểm tra chồng lấn với các bậc đã có. Test này ĐỎ cho tới khi bổ sung validation.
        [Fact]
        public async Task L1_DT_07_OverlappingRange_IsRejected()
        {
            _db.DiscountTiers.Add(TestData.DiscountTier(10_000_000m, 50_000_000m, 0.03m));
            _db.SaveChanges();

            var act = () => _sut.CreateAsync(
                new CreateDiscountTierRequest { MinAmount = 30_000_000m, MaxAmount = 60_000_000m, DiscountPercent = 0.04m },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("bậc 30tr–60tr chồng lấn bậc 10tr–50tr đã có");
            _db.DiscountTiers.Should().HaveCount(1, "bảng bậc không được thay đổi khi bị từ chối");
        }

        // L1-DT-08 | EP-Invalid | Tạo bậc có % ngoài khoảng hợp lệ -> từ chối validation
        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public async Task L1_DT_08_PercentOutOfRange_IsRejected(int percent)
        {
            var act = () => _sut.CreateAsync(
                new CreateDiscountTierRequest { MinAmount = 10_000_000m, MaxAmount = 50_000_000m, DiscountPercent = percent },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>();
            _db.DiscountTiers.Should().BeEmpty();
        }

        // L1-DT-09 | EP-Valid | Sửa bậc phải để lại vết before/after trong audit — lịch sử vẫn tra cứu được
        [Fact]
        public async Task L1_DT_09_Update_LeavesAuditTrailOfPreviousValue()
        {
            var tier = TestData.DiscountTier(10_000_000m, 50_000_000m, 0.03m);
            _db.DiscountTiers.Add(tier);
            _db.SaveChanges();

            await _sut.UpdateAsync(tier.Id,
                new UpdateDiscountTierRequest { MinAmount = 10_000_000m, MaxAmount = 50_000_000m, DiscountPercent = 0.06m, IsActive = true },
                ActorId, ActorEmail, ActorIp);

            _audit.Verify(a => a.LogAsync(
                "DiscountTier", tier.Id.ToString(), "UPDATE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.Is<object>(before => ((DiscountTierDto)before).DiscountPercent == 0.03m),
                It.Is<object>(after => ((DiscountTierDto)after).DiscountPercent == 0.06m),
                It.IsAny<string>(), ActorIp), Times.Once);
        }
    }
}
