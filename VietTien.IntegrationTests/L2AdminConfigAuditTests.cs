using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Auth;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Sheet L2-AdminConfigAudit — cấu hình có phiên bản, nhật ký kiểm toán, quản trị người dùng,
    /// phạm vi dashboard và KPI theo kỳ.
    ///
    /// ⚠ ADM-07: SRS gọi là "KPI snapshot theo kỳ"; thực tế KPI lộ qua 3 endpoint dashboard
    /// (`/api/dashboards/{role}?from=&to=`) — IKpiService.GetSnapshotAsync được 3 dashboard service
    /// gọi. Lệch THUẬT NGỮ tài liệu, không phải thiếu chức năng.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2AdminConfigAuditTests : SqlServerTestBase
    {
        public L2AdminConfigAuditTests(SqlServerFixture factory) : base(factory) { }

        private const string ConfigKey = "SEPAY_RESERVATION_MINUTES";

        // ── L2-ADM-01 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Có cấu hình đang hiệu lực và một đơn đã hoàn tất
        // WHEN   PUT đổi cấu hình qua API, rồi đọc lại đơn đã hoàn tất
        // THEN   Tạo phiên bản cấu hình MỚI, bản cũ vẫn còn; đơn cũ không bị tính lại; có AuditLog before/after
        [Fact]
        [Trait("TestID", "L2-ADM-01")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 AC-03; NAC-04; BR-050")]
        public async Task L2_ADM_01_ConfigChangeIsVersionedAndDoesNotRepriceClosedOrders()
        {
            await ResetAsync();
            var (admin, adminUser) = await CreateClientAsAsync(SystemRole.Admin);

            Guid orderId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 100;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Orders.Add(new Order
                {
                    Id = orderId, CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 3_000_000m, DiscountAmount = 150_000m, FinalPayment = 2_850_000m,
                    PaymentMethod = PaymentMethod.SePay, PaymentStatus = PaymentStatus.Paid,
                    OrderStatus = OrderStatus.Completed, CreatedAt = DateTime.UtcNow.AddDays(-2),
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = inv.ProductId!.Value, Quantity = 1, PriceSnapshot = 3_000_000m, CostSnapshot = 0m }
                    }
                });
            });

            var versionsBefore = await QueryAsync(db => db.SystemConfigVersions.CountAsync(v => v.ConfigKey == ConfigKey));
            var auditBefore = await QueryAsync(db => db.AuditLogs.CountAsync());
            var orderBefore = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));

            var update = await admin.PutAsJsonAsync($"/api/admin/system-configs/{ConfigKey}",
                new UpdateSystemConfigRequest { Value = "20", Reason = "kiem thu L2" });

            // (a) HTTP
            update.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await update.Content.ReadAsStringAsync());

            // (b) DB — phiên bản mới được tạo, bản cũ giữ nguyên
            var versionsAfter = await QueryAsync(db => db.SystemConfigVersions.AsNoTracking()
                .Where(v => v.ConfigKey == ConfigKey).ToListAsync());
            versionsAfter.Count.Should().BeGreaterThan(versionsBefore,
                "BR-050: đổi cấu hình phải sinh phiên bản MỚI, không ghi đè");
            versionsAfter.Should().Contain(v => v.Value == "20", "phiên bản mới phải có giá trị mới");

            // Đơn đã hoàn tất không bị tính lại
            var orderAfter = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
            orderAfter.FinalPayment.Should().Be(orderBefore.FinalPayment,
                "NAC-04: đổi cấu hình không được tính lại đơn đã chốt");
            orderAfter.DiscountAmount.Should().Be(orderBefore.DiscountAmount);

            // (c) side effect — AuditLog kèm before/after
            var audits = await QueryAsync(db => db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName.Contains("Config") || a.EntityId.Contains(ConfigKey)).ToListAsync());
            (await QueryAsync(db => db.AuditLogs.CountAsync())).Should().BeGreaterThan(auditBefore,
                "BR-050: phải ghi AuditLog cho thay đổi cấu hình");
            audits.Should().Contain(a => !string.IsNullOrEmpty(a.BeforeJson) || !string.IsNullOrEmpty(a.AfterJson),
                "AuditLog phải lưu giá trị trước/sau");
        }

        // ── L2-ADM-02 ── N/A phần ép lỗi ───────────────────────────────────────────────────

        // GIVEN  Một cấu hình được kiểm soát
        // WHEN   PUT đổi cấu hình
        // THEN   Cấu hình và AuditLog luôn đi cùng nhau trong một giao dịch
        [Fact]
        [Trait("TestID", "L2-ADM-02")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 AC-04; BR-022")]
        [Trait("Partial", "N/A-fault-injection")]
        public async Task L2_ADM_02_ConfigChangeAlwaysCarriesAuditRecord()
        {
            await ResetAsync();
            var (admin, _) = await CreateClientAsAsync(SystemRole.Admin);

            var auditBefore = await QueryAsync(db => db.AuditLogs.CountAsync());
            // Giá trị hiệu lực nằm ở phiên bản mới nhất (SystemConfig chỉ giữ metadata, không giữ Value).
            var valueBefore = await QueryAsync(db => db.SystemConfigVersions.AsNoTracking()
                .Where(v => v.ConfigKey == ConfigKey)
                .OrderByDescending(v => v.EffectiveDate).Select(v => v.Value).FirstAsync());

            var update = await admin.PutAsJsonAsync($"/api/admin/system-configs/{ConfigKey}",
                new UpdateSystemConfigRequest { Value = "25", Reason = "kiem thu L2" });
            update.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await update.Content.ReadAsStringAsync());

            var valueAfter = await QueryAsync(db => db.SystemConfigVersions.AsNoTracking()
                .Where(v => v.ConfigKey == ConfigKey)
                .OrderByDescending(v => v.EffectiveDate).Select(v => v.Value).FirstAsync());
            var auditAfter = await QueryAsync(db => db.AuditLogs.CountAsync());

            // BR-022: đổi được cấu hình thì BẮT BUỘC có vết audit đi kèm — hai thứ không được rời nhau.
            valueAfter.Should().NotBe(valueBefore, "cấu hình phải đổi thật");
            auditAfter.Should().BeGreaterThan(auditBefore,
                "BR-022: audit là điều kiện bắt buộc của giao dịch đổi cấu hình");

            // ⚠ Vế "ép lỗi ghi AuditLog rồi kiểm rollback" KHÔNG kiểm được ở L2: không có seam nào
            // để tiêm lỗi vào riêng bước ghi audit mà không đụng schema dùng chung của cả suite.
            // Cần fault-injection ở L1 với IAuditLogService giả. Xem danh sách N/A trong báo cáo.
        }

        // ── L2-ADM-03 ── một nửa N/A ───────────────────────────────────────────────────────

        // GIVEN  Đã có bản ghi AuditLog
        // WHEN   Thử sửa/xoá qua API, và thử UPDATE thẳng DB
        // THEN   API: không tồn tại đường sửa/xoá; DB: ghi nhận có/không ràng buộc ở tầng schema
        [Fact]
        [Trait("TestID", "L2-ADM-03")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 NAC-03; BR-048; NFR-SEC08")]
        [Trait("Partial", "N/A-db-permission")]
        public async Task L2_ADM_03_AuditLogHasNoMutationEndpoint()
        {
            await ResetAsync();
            var (admin, _) = await CreateClientAsAsync(SystemRole.Admin);

            // Sinh ít nhất 1 bản ghi audit
            (await admin.PutAsJsonAsync($"/api/admin/system-configs/{ConfigKey}",
                new UpdateSystemConfigRequest { Value = "30", Reason = "kiem thu L2" })).EnsureSuccessStatusCode();

            var log = await QueryAsync(db => db.AuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstAsync());
            var countBefore = await QueryAsync(db => db.AuditLogs.CountAsync());

            // (a) API — KHÔNG được tồn tại đường sửa/xoá audit log
            var put = await admin.PutAsJsonAsync($"/api/admin/audit-logs/{log.Id}", new { reason = "sua trom" });
            var delete = await admin.DeleteAsync($"/api/admin/audit-logs/{log.Id}");

            ((int)put.StatusCode).Should().BeInRange(400, 499,
                "NFR-SEC08: không được có endpoint sửa audit log; nhận {0}", put.StatusCode);
            ((int)delete.StatusCode).Should().BeInRange(400, 499,
                "NFR-SEC08: không được có endpoint xoá audit log; nhận {0}", delete.StatusCode);

            // (b) DB — số bản ghi không đổi sau các nỗ lực trên
            (await QueryAsync(db => db.AuditLogs.CountAsync())).Should().Be(countBefore);
            var reread = await QueryAsync(db => db.AuditLogs.AsNoTracking().FirstAsync(a => a.Id == log.Id));
            reread.Action.Should().Be(log.Action, "bản ghi audit không được đổi");

            // (c) Vế tầng DB — bất biến nay ĐƯỢC CƯỠNG CHẾ Ở SCHEMA.
            // Trước 13/08/2026 bảng AuditLogs không có ràng buộc nào, nên `sa` UPDATE được và ô này
            // chỉ ghi nhận "chưa cưỡng chế ở tầng DB", không kết luận defect. Migration
            // 20260813035338_AddAuditLogInsertOnlyTrigger đã dựng trigger INSTEAD OF UPDATE/DELETE,
            // chặn kể cả tài khoản toàn quyền -> đây mới đúng NFR-SEC08/BR-048.
            await using var conn = new SqlConnection(Factory.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE AuditLogs SET Reason = N'sa-da-sua' WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", log.Id);

            var update = async () => await cmd.ExecuteNonQueryAsync();
            await update.Should().ThrowAsync<SqlException>(
                "NFR-SEC08: trigger phải chặn UPDATE audit log kể cả với tài khoản toàn quyền");

            // Và nội dung thật sự không đổi.
            (await QueryAsync(db => db.AuditLogs.AsNoTracking().FirstAsync(a => a.Id == log.Id)))
                .Reason.Should().NotBe("sa-da-sua");
        }

        // ── L2-ADM-04 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Admin JWT; email nhân viên mới chưa tồn tại
        // WHEN   Tạo tài khoản → đổi vai trò → vô hiệu hoá → thử dùng refresh token cũ
        // THEN   PasswordHash khác plaintext; có vết audit; sau khi vô hiệu hoá, refresh token bị thu hồi
        [Fact]
        [Trait("TestID", "L2-ADM-04")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 AC-04; BR-022; NFR-SEC02")]
        public async Task L2_ADM_04_UserLifecycleIsAuditedAndDisablingRevokesRefreshToken()
        {
            await ResetAsync();
            var (admin, _) = await CreateClientAsAsync(SystemRole.Admin);

            var email = $"staff.{Guid.NewGuid():N}@test.local";
            const string password = "P@ss123456";
            var auditBefore = await QueryAsync(db => db.AuditLogs.CountAsync());

            // 1) Tạo tài khoản nhân viên
            var create = await admin.PostAsJsonAsync("/api/admin/users", new CreateStaffUserRequest
            {
                FullName = "Nhan vien moi",
                Email = email,
                PhoneNumber = "09" + Random.Shared.Next(0, 100_000_000).ToString("D8"),
                Password = password,
                Role = nameof(SystemRole.SalesStaff)
            });
            create.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await create.Content.ReadAsStringAsync());

            var created = await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Email == email));
            created.PasswordHash.Should().NotBe(password, "NFR-SEC04: không lưu mật khẩu thô");
            created.Role.Should().Be(SystemRole.SalesStaff);

            // Đăng nhập để có refresh token
            var login = await Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new LoginDto { Email = email, Password = password });
            login.StatusCode.Should().Be(HttpStatusCode.OK, "body: {0}", await login.Content.ReadAsStringAsync());
            var refreshToken = await QueryAsync(db => db.Users.AsNoTracking()
                .Where(u => u.Id == created.Id).Select(u => u.RefreshToken).FirstAsync());
            refreshToken.Should().NotBeNullOrEmpty();

            // 2) Đổi vai trò
            var changeRole = await admin.PutAsJsonAsync($"/api/admin/users/{created.Id}/role",
                new ChangeUserRoleRequest { NewRole = nameof(SystemRole.AccountingStaff), Reason = "dieu chuyen" });
            changeRole.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await changeRole.Content.ReadAsStringAsync());
            (await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == created.Id)))
                .Role.Should().Be(SystemRole.AccountingStaff);

            // 3) Vô hiệu hoá tài khoản
            var disable = await admin.PutAsJsonAsync($"/api/admin/users/{created.Id}/status",
                new SetUserActiveStatusRequest { IsActive = false, Reason = "nghi viec" });
            disable.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await disable.Content.ReadAsStringAsync());

            var disabled = await QueryAsync(db => db.Users.AsNoTracking().FirstAsync(u => u.Id == created.Id));
            disabled.IsActive.Should().BeFalse();

            // 4) Refresh token cũ phải bị thu hồi
            disabled.RefreshToken.Should().BeNull(
                "NFR-SEC02: vô hiệu hoá tài khoản phải thu hồi refresh token đang có");

            var refreshAttempt = await Factory.CreateClient().PostAsJsonAsync("/api/auth/refresh-token",
                new RefreshTokenDto { RefreshToken = refreshToken! });
            refreshAttempt.IsSuccessStatusCode.Should().BeFalse(
                "tài khoản đã vô hiệu hoá không được refresh; body: {0}",
                await refreshAttempt.Content.ReadAsStringAsync());

            // (c) side effect — 3 thao tác quản trị đều có vết audit
            (await QueryAsync(db => db.AuditLogs.CountAsync()))
                .Should().BeGreaterThanOrEqualTo(auditBefore + 3,
                    "BR-022: tạo/đổi vai trò/vô hiệu hoá đều phải ghi AuditLog");
        }

        // ── L2-ADM-05 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Admin JWT; có báo giá đang chờ Sales Manager duyệt
        // WHEN   Gọi thẳng API duyệt báo giá bằng JWT Admin
        // THEN   403; DB không đổi trạng thái nghiệp vụ
        [Fact]
        [Trait("TestID", "L2-ADM-05")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 NAC-02; NFR-SEC03")]
        public async Task L2_ADM_05_AdminCannotActAsBusinessApprover()
        {
            await ResetAsync();
            var (admin, _) = await CreateClientAsAsync(SystemRole.Admin);
            var (_, salesUser) = await CreateClientAsAsync(SystemRole.SalesStaff);

            var quotationId = Guid.NewGuid();
            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Quotations.Add(new Quotation
                {
                    Id = quotationId, CustomerProfileId = profileId, SalesStaffId = salesUser.Id,
                    Status = QuotationStatus.PendingManager, OriginalTotal = 120_000_000m,
                    RequestDate = DateTime.UtcNow.AddHours(-1), ValidUntil = DateTime.UtcNow.AddDays(3)
                });
                await Task.CompletedTask;
            });

            var response = await admin.PostAsJsonAsync($"/api/Quotation/{quotationId}/manager-decision",
                new ManagerReviewRequest { IsApproved = true, ManagerNote = "admin tu duyet" });

            // (a) HTTP — NFR-SEC03: Admin là vai trò quản trị hệ thống, không phải người duyệt nghiệp vụ
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "Admin không được thay Sales Manager duyệt báo giá; body: {0}",
                await response.Content.ReadAsStringAsync());

            // (b) DB
            (await QueryAsync(db => db.Quotations.AsNoTracking().FirstAsync(q => q.Id == quotationId)))
                .Status.Should().Be(QuotationStatus.PendingManager, "trạng thái nghiệp vụ không được đổi");
        }

        // ── L2-ADM-06 ──────────────────────────────────────────────────────────────────────

        // GIVEN  S1 và S2 là hai Sales khác nhau, mỗi người phụ trách khách riêng
        // WHEN   S1 gọi dashboard Sales Staff và cố truyền id của S2
        // THEN   Không trả dữ liệu của S2 — phạm vi luôn bị ép về chính S1
        [Fact]
        [Trait("TestID", "L2-ADM-06")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "FT-09 NAC-01; BV-01; NFR-SEC03")]
        public async Task L2_ADM_06_SalesDashboardIsAlwaysScopedToCaller()
        {
            await ResetAsync();
            var (s1Client, s1) = await CreateClientAsAsync(SystemRole.SalesStaff);
            var (_, s2) = await CreateClientAsAsync(SystemRole.SalesStaff);

            // Đơn thuộc phạm vi của S2, KHÔNG thuộc S1
            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 100;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                db.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(), CustomerProfileId = profileId,
                    OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                    TotalAmount = 77_777_777m, FinalPayment = 77_777_777m,
                    PaymentMethod = PaymentMethod.SePay, PaymentStatus = PaymentStatus.Paid,
                    OrderStatus = OrderStatus.Completed, CreatedAt = DateTime.UtcNow.AddDays(-1),
                    SalesStaffId = s2.Id,
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = inv.ProductId!.Value, Quantity = 1, PriceSnapshot = 77_777_777m, CostSnapshot = 0m }
                    }
                });
            });

            // S1 cố ép phạm vi sang S2 bằng query string
            var response = await s1Client.GetAsync($"/api/dashboards/sales-staff?salesStaffId={s2.Id}&userId={s2.Id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            var body = await response.Content.ReadAsStringAsync();

            // (a)+(b) — endpoint không nhận id nào, luôn dùng GetUserId() từ JWT
            body.Should().NotContain("77777777",
                "NFR-SEC03: S1 không được thấy doanh số của S2 dù truyền id S2 lên query string");
            body.Should().NotContain(s2.Id.ToString(),
                "không được rò rỉ dữ liệu gắn với S2");
        }

        // ── L2-ADM-07 ──────────────────────────────────────────────────────────────────────

        // GIVEN  Đơn đúng đầu kỳ và cuối kỳ, cùng 1 đơn trước kỳ và 1 đơn sau kỳ; thêm 1 đơn đã huỷ
        // WHEN   GET KPI theo kỳ (qua dashboard có from/to)
        // THEN   Hai đơn biên được tính, hai đơn ngoài kỳ bị loại, đơn Cancelled không vào doanh thu
        [Fact]
        [Trait("TestID", "L2-ADM-07")]
        [Trait("Priority", "P2")]
        [Trait("SRSRef", "FT-09 AC-01")]
        public async Task L2_ADM_07_KpiPeriodIncludesBoundariesAndExcludesCancelled()
        {
            await ResetAsync();
            var (ceo, _) = await CreateClientAsAsync(SystemRole.CEO);

            var from = DateTime.UtcNow.Date.AddDays(-7);
            var to = DateTime.UtcNow.Date.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);

            await SeedAsync(async db =>
            {
                var inv = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inv.OnHandQuantity = 1000;
                var productId = inv.ProductId!.Value;
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();

                void Add(decimal amount, DateTime createdAt, OrderStatus status)
                {
                    db.Orders.Add(new Order
                    {
                        Id = Guid.NewGuid(), CustomerProfileId = profileId,
                        OrderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                        TotalAmount = amount, FinalPayment = amount,
                        PaymentMethod = PaymentMethod.SePay, PaymentStatus = PaymentStatus.Paid,
                        OrderStatus = status, CreatedAt = createdAt,
                        OrderItems = new List<OrderItem>
                        {
                            new() { ProductId = productId, Quantity = 1, PriceSnapshot = amount, CostSnapshot = 0m }
                        }
                    });
                }

                Add(1_000_000m, from, OrderStatus.Completed);                    // biên đầu kỳ — TÍNH
                Add(2_000_000m, to, OrderStatus.Completed);                      // biên cuối kỳ — TÍNH
                Add(4_000_000m, from.AddDays(-1), OrderStatus.Completed);        // trước kỳ — LOẠI
                Add(8_000_000m, to.AddDays(2), OrderStatus.Completed);           // sau kỳ — LOẠI
                Add(16_000_000m, from.AddDays(1), OrderStatus.Cancelled);        // đã huỷ — KHÔNG vào doanh thu
                await Task.CompletedTask;
            });

            var response = await ceo.GetAsync(
                $"/api/dashboards/ceo?from={from:yyyy-MM-ddTHH:mm:ss}&to={to:yyyy-MM-ddTHH:mm:ss}");

            // (a) HTTP
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadAsStringAsync();

            // (b) hai đơn ngoài kỳ và đơn huỷ không được góp vào con số của kỳ
            body.Should().NotContain("4000000", "đơn trước kỳ phải bị loại");
            body.Should().NotContain("8000000", "đơn sau kỳ phải bị loại");
            body.Should().NotContain("16000000", "đơn đã huỷ không được tính vào doanh thu");
        }
    }
}
