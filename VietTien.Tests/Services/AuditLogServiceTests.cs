using System.Reflection;
using FluentAssertions;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: AuditLogService — L1-AUD-01..06. EF InMemory.
    /// ⚠ L1-AUD-07 (export bởi vai trò không được phép -> forbidden): ExportCsvAsync(query) KHÔNG có
    ///    tham số callerRole — phân quyền nằm ở [Authorize] tầng Controller nên không unit-test được
    ///    ở đây -> đã chuyển sang L3: VietTien.IntegrationTests/RoleGateTests.cs (L1_AUD_07_*).
    /// Signature thật của LogAsync có 10 tham số: entityName, entityId, action, actorUserId,
    /// actorEmail, actorRole, before, after, reason, ipAddress.
    /// </summary>
    public class AuditLogServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly AuditLogService _sut;

        public AuditLogServiceTests()
        {
            _sut = new AuditLogService(_db);
        }

        // ── Block: LogAsync() ───────────────────────────────────────────────

        // L1-AUD-01 | EP-Valid | Ghi log đủ actor, hành động, thực thể, thời điểm, before/after
        [Fact]
        public async Task L1_AUD_01_Log_StoresAllFiveRequiredParts()
        {
            var actorId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            await _sut.LogAsync("Product", productId.ToString(), "PriceChange",
                actorId, "admin@viettien.com", "Admin",
                before: new { Price = 50_000m }, after: new { Price = 55_000m },
                reason: "Điều chỉnh giá niêm yết", ipAddress: "127.0.0.1");

            var log = _db.AuditLogs.Single();
            log.EntityName.Should().Be("Product");
            log.EntityId.Should().Be(productId.ToString());
            log.Action.Should().Be("PriceChange");
            log.ActorUserId.Should().Be(actorId);
            log.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            log.BeforeJson.Should().Contain("50000");
            log.AfterJson.Should().Contain("55000");
        }

        // L1-AUD-02 | EP-Invalid | Không có actorUserId -> phải ghi rõ HỆ THỐNG là actor,
        // không được tạo bản ghi khuyết hoàn toàn thông tin người thực hiện.
        [Fact]
        public async Task L1_AUD_02_NullActorUser_StillIdentifiesSystemAsActor()
        {
            await _sut.LogAsync("Order", Guid.NewGuid().ToString(), "Cancel",
                actorUserId: null, actorEmail: null, actorRole: "System",
                before: null, after: null, reason: "Huỷ tự động do hết hạn giữ tồn");

            var log = _db.AuditLogs.Single();
            (log.ActorUserId != null || !string.IsNullOrWhiteSpace(log.ActorRole))
                .Should().BeTrue("bản ghi audit phải xác định được ai/cái gì đã thực hiện hành động");
            log.ActorRole.Should().Be("System");
        }

        // L1-AUD-03 | EP-Valid | Audit log là APPEND-ONLY: interface không hề có API sửa/xoá
        [Fact]
        public void L1_AUD_03_Interface_IsAppendOnly()
        {
            var methodNames = typeof(IAuditLogService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name)
                .ToList();

            methodNames.Should().NotContain(n =>
                n.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("Remove", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("Edit", StringComparison.OrdinalIgnoreCase));

            methodNames.Should().BeEquivalentTo(new[] { "LogAsync", "SearchAsync", "ExportCsvAsync" });
        }

        // L1-AUD-04 | EP-Valid | PII trong log phải được mask (SĐT, MST) và tuyệt đối không lưu token/secret
        // 🔴 SPEC GAP v2.2: SensitiveDataRedactor chỉ mask theo TÊN FIELD chứa
        // password/otp/token/secret/apikey/pin. Số điện thoại và mã số thuế lưu NGUYÊN VĂN.
        // NFR-SEC06 yêu cầu mask PII -> test ĐỎ cho tới khi bổ sung mask cho PhoneNumber/TaxCode.
        [Fact]
        public async Task L1_AUD_04_PiiIsMasked()
        {
            await _sut.LogAsync("CustomerProfile", Guid.NewGuid().ToString(), "UPDATE",
                Guid.NewGuid(), "sales@viettien.com", "SalesStaff",
                before: new { PhoneNumber = "0912345678", TaxCode = "0101234567", AccessToken = "secret-jwt" },
                after: new { PhoneNumber = "0987654321", TaxCode = "0101234567", AccessToken = "secret-jwt" });

            var log = _db.AuditLogs.Single();

            log.BeforeJson.Should().NotContain("secret-jwt", "token luôn phải bị redact");
            log.BeforeJson.Should().NotContain("0912345678", "SĐT là PII, phải được mask (vd 0912***678)");
            log.BeforeJson.Should().NotContain("0101234567", "mã số thuế là PII, phải được mask");
        }

        // ── Block: SearchAsync() / ExportCsvAsync() ─────────────────────────

        // L1-AUD-05 | EP-Valid | Search lọc theo thực thể + khoảng thời gian
        [Fact]
        public async Task L1_AUD_05_Search_FiltersByEntityAndDateRange()
        {
            var now = DateTime.UtcNow;
            _db.AuditLogs.AddRange(
                TestData.AuditLog("Order", "CREATE", l => l.CreatedAt = now.AddDays(-1)),
                TestData.AuditLog("Order", "CANCEL", l => l.CreatedAt = now.AddDays(-1)),
                TestData.AuditLog("Order", "CREATE", l => l.CreatedAt = now.AddDays(-30)), // ngoài khoảng
                TestData.AuditLog("Product", "UPDATE", l => l.CreatedAt = now.AddDays(-1)),
                TestData.AuditLog("SystemConfig", "CONFIG_CHANGE", l => l.CreatedAt = now.AddDays(-1)));
            _db.SaveChanges();

            var result = await _sut.SearchAsync(new AuditLogQueryDto
            {
                EntityName = "Order",
                FromDate = now.AddDays(-7),
                ToDate = now
            });

            result.TotalCount.Should().Be(2);
            result.Items.Should().OnlyContain(i => i.EntityName == "Order");
        }

        // L1-AUD-06 | EP-Valid | Export CSV escape dấu phẩy và dấu ngoặc kép, không vỡ cột
        [Fact]
        public async Task L1_AUD_06_ExportCsv_EscapesSpecialCharacters()
        {
            _db.AuditLogs.Add(TestData.AuditLog("Order", "CANCEL",
                l => l.Reason = "Khách đổi ý, yêu cầu huỷ \"gấp\""));
            _db.SaveChanges();

            var bytes = await _sut.ExportCsvAsync(new AuditLogQueryDto());
            var csv = System.Text.Encoding.UTF8.GetString(bytes);

            csv.Should().Contain("\"Khách đổi ý, yêu cầu huỷ \"\"gấp\"\"\"",
                "dấu phẩy phải được bọc trong ngoặc kép và ngoặc kép phải được nhân đôi");
            csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2, "1 dòng header + 1 dòng dữ liệu");
        }
    }
}
