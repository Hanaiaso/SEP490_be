using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Models;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Sheet <c>L3-AdminConfigAPI</c> — ADMC-01..16.
    /// (ADMC-17 kiểm bằng Newman vì Swagger chỉ bật khi ASPNETCORE_ENVIRONMENT=Development.)
    ///
    /// Ánh xạ chính: /api/admin/system-health/jobs -> /job-runs;
    /// /api/admin/configurations/discount-tier -> /api/admin/discount-tiers/{id}.
    /// </summary>
    public class L3AdminConfigApiTests : L3TestBase
    {
        public L3AdminConfigApiTests(L3SqlFixture factory) : base(factory) { }

        /// ADMC-01 | Input-Domain-Happy | FT-09 AC-04; NFR-SEC02
        /// Admin tạo tài khoản -> đúng vai trò yêu cầu, mật khẩu lưu dạng hash.
        [Fact]
        public async Task L3_ADMC_01_CreateUser_ByAdmin_HashesPasswordAndSetsRole()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var email = NewEmail();

            var res = await admin.PostAsJsonAsync("/api/admin/users", new
            {
                FullName = "Nhan vien moi",
                Email = email,
                PhoneNumber = NewPhone(),
                Password = "Passw0rd!",
                Role = nameof(SystemRole.SalesStaff),
            });

            res.IsSuccessStatusCode.Should().BeTrue(
                $"Admin phải tạo được tài khoản ({(int)res.StatusCode}: {await ReadMessageAsync(res)})");

            var created = await QueryAsync(db => db.Users.SingleAsync(u => u.Email == email));
            created.Role.Should().Be(SystemRole.SalesStaff);
            created.PasswordHash.Should().NotBe("Passw0rd!").And.StartWith("$2",
                "mật khẩu phải được băm, không lưu plaintext");
        }

        /// ADMC-02 | Input-Domain-Error | NFR-SEC03
        /// Sales Manager tạo tài khoản -> 403 (chỉ Admin).
        [Fact]
        public async Task L3_ADMC_02_CreateUser_BySalesManager_Forbidden()
        {
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);

            var res = await manager.PostAsJsonAsync("/api/admin/users", new
            {
                FullName = "X", Email = NewEmail(), PhoneNumber = NewPhone(),
                Password = "Passw0rd!", Role = nameof(SystemRole.SalesStaff),
            });

            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// ADMC-03 | Input-Domain-Error | FT-09 AC-04; FT-04 NAC-03
        /// Hạ vai trò Sale đang phụ trách khách -> phải yêu cầu bàn giao trước, vai trò KHÔNG đổi.
        [Fact]
        public async Task L3_ADMC_03_DemoteSalesStaffWithAssignedCustomers_Rejected_RoleUnchanged()
        {
            // Gán 1 khách cho Sales Staff.
            var (_, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            await SeedAsync(async db =>
            {
                var p = await db.CustomerProfiles.SingleAsync(x => x.Id == profile.Id);
                p.AssignedSalesStaffId = L3Seed.SalesStaffId;
            });

            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var res = await admin.PutAsJsonAsync($"/api/admin/users/{L3Seed.SalesStaffId}/role",
                new { Role = nameof(SystemRole.Customer), Reason = "ha vai tro" });

            res.IsSuccessStatusCode.Should().BeFalse("phải bàn giao khách trước khi hạ vai trò");
            (await QueryAsync(db => db.Users.SingleAsync(u => u.Id == L3Seed.SalesStaffId)))
                .Role.Should().Be(SystemRole.SalesStaff, "vai trò không được đổi");
        }

        /// ADMC-04 | Input-Domain-Happy | FT-09 AC-03; BR-050
        /// Cập nhật cấu hình có hiệu lực TƯƠNG LAI -> tạo phiên bản mới, lịch sử giữ cả bản cũ.
        [Fact]
        public async Task L3_ADMC_04_UpdateSystemConfig_FutureEffective_CreatesNewVersionKeepsHistory()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            const string key = "QUOTATION_MIN_VALUE";
            var versionsBefore = await QueryAsync(db => db.SystemConfigVersions.CountAsync(v => v.ConfigKey == key));

            var res = await admin.PutAsJsonAsync($"/api/admin/system-configs/{key}", new
            {
                Value = "120000000",
                EffectiveDate = DateTime.UtcNow.AddDays(1),
                Reason = "Nang nguong bao gia",
            });

            res.IsSuccessStatusCode.Should().BeTrue(
                $"cập nhật hiệu lực tương lai phải thành công ({await ReadMessageAsync(res)})");
            (await QueryAsync(db => db.SystemConfigVersions.CountAsync(v => v.ConfigKey == key)))
                .Should().BeGreaterThan(versionsBefore, "phải tạo phiên bản mới, không ghi đè");

            var history = await admin.GetAsync($"/api/admin/system-configs/{key}/history");
            history.StatusCode.Should().Be(HttpStatusCode.OK);
            (await history.Content.ReadAsStringAsync()).Should().Contain("120000000");
        }

        /// ADMC-05 | BVA | FT-09 BV-02; NAC-04; BR-050
        /// Workbook: GET /{key}/effective?at= với at = T-1s / T / T+1s.
        /// Hệ thống KHÔNG có endpoint tra cứu theo mốc thời gian — chỉ có lịch sử phiên bản.
        /// Kiểm bất biến tương đương: cấu hình hiệu lực tương lai CHƯA được áp dụng ở hiện tại.
        [Fact]
        public async Task L3_ADMC_05_EffectiveAtEndpointMissing_FutureConfigNotAppliedYet()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            const string key = "QUOTATION_MIN_VALUE";

            (await admin.GetAsync($"/api/admin/system-configs/{key}/effective?at={DateTime.UtcNow:O}"))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            // ^ không có API tra cứu giá trị hiệu lực theo mốc thời gian

            await admin.PutAsJsonAsync($"/api/admin/system-configs/{key}", new
            {
                Value = "500000000",
                EffectiveDate = DateTime.UtcNow.AddDays(30),
                Reason = "Hieu luc xa trong tuong lai",
            });

            // Ngưỡng đang áp dụng vẫn phải là 100 triệu: giỏ 100 triệu vẫn bị chặn đòi báo giá.
            var (client, user) = await CreateClientAsAsync(SystemRole.Customer);
            var profile = await EnsureProfileAsync(user.Id);
            var (product, _) = await SeedSellableProductAsync(100_000_000m, 10);
            await SeedCartAsync(profile.Id, null, (product.Id, 1, 100_000_000m));

            var summary = await client.GetAsync("/api/orders/checkout-summary");

            summary.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "cấu hình hiệu lực 30 ngày nữa KHÔNG được áp dụng ngay hôm nay");
        }

        /// ADMC-06 | Input-Domain-Error | FT-09 NAC-04
        /// effectiveFrom trong QUÁ KHỨ -> bị từ chối (không cho sửa hồi tố).
        [Fact]
        public async Task L3_ADMC_06_UpdateSystemConfig_RetroactiveEffectiveFrom_Rejected()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            const string key = "LIST_PRICE_MAX_EXCLUSIVE";
            var before = await QueryAsync(db => db.SystemConfigVersions
                .Where(v => v.ConfigKey == key).OrderByDescending(v => v.EffectiveDate).FirstAsync());

            var res = await admin.PutAsJsonAsync($"/api/admin/system-configs/{key}", new
            {
                Value = "1",
                EffectiveDate = DateTime.UtcNow.AddDays(-30),
                Reason = "Sua hoi to",
            });

            res.IsSuccessStatusCode.Should().BeFalse("không được đặt hiệu lực trong quá khứ");
            (await QueryAsync(db => db.SystemConfigVersions
                    .Where(v => v.ConfigKey == key).OrderByDescending(v => v.EffectiveDate).FirstAsync()))
                .Value.Should().Be(before.Value, "giá trị cấu hình không được đổi");
        }

        /// ADMC-07 | Input-Domain-Error | FT-09 NAC-03; BR-048; NFR-SEC08  ->  nhóm B
        [Fact]
        public async Task L3_ADMC_07_DeleteAuditLog_RouteDoesNotExist()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            (await admin.DeleteAsync($"/api/admin/audit-logs/{Guid.NewGuid()}"))
                .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        /// ADMC-08 | Input-Domain-Happy | FT-09 AC-04
        /// Xuất CSV audit log: bản ghi có dấu phẩy/ngoặc kép trong lý do không được làm vỡ cột.
        [Fact]
        public async Task L3_ADMC_08_ExportAuditLogCsv_HandlesCommasAndQuotes()
        {
            await SeedAsync(db =>
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EntityName = "SystemConfig",
                    EntityId = "QUOTATION_MIN_VALUE",
                    Action = "UPDATE",
                    ActorUserId = L3Seed.AdminId,
                    ActorEmail = L3Seed.AdminEmail,
                    ActorRole = nameof(SystemRole.Admin),
                    Reason = "Ly do co dau phay, va \"ngoac kep\" ben trong",
                    CreatedAt = DateTime.UtcNow,
                });
                return Task.CompletedTask;
            });

            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var res = await admin.GetAsync("/api/admin/audit-logs/export");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var csv = await res.Content.ReadAsStringAsync();
            csv.Should().Contain("\"\"ngoac kep\"\"",
                "ngoặc kép trong dữ liệu phải được escape thành \"\" theo chuẩn CSV");
        }

        /// ADMC-09 | Input-Domain-Happy | FT-07 BV-01; BR-037
        /// Danh sách xe đọc được bởi Sales Staff (dữ liệu nền để xếp lịch giao).
        [Fact]
        public async Task L3_ADMC_09_ListVehicles_ReadableBySalesStaff()
        {
            var sales = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var res = await sales.GetAsync("/api/vehicles?active=true");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            (await QueryAsync(db => db.Vehicles.CountAsync())).Should().BeGreaterThan(0, "phải có xe seed sẵn");
        }

        /// ADMC-10 | Input-Domain-Error | BR-037
        /// Tạo xe trùng biển số -> bị từ chối, không tạo bản ghi thứ 2.
        [Fact]
        public async Task L3_ADMC_10_CreateVehicle_DuplicatePlate_Rejected()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var plate = "51H-" + Random.Shared.Next(10000, 99999);

            var vehicleNumber = Random.Shared.Next(100, 9999);
            var first = await admin.PostAsJsonAsync("/api/vehicles",
                new { VehicleNumber = vehicleNumber, LicensePlate = plate, Capacity = 1000m, IsActive = true });
            first.IsSuccessStatusCode.Should().BeTrue(
                $"xe đầu tiên phải tạo được ({await ReadMessageAsync(first)})");

            var second = await admin.PostAsJsonAsync("/api/vehicles",
                new { VehicleNumber = vehicleNumber + 1, LicensePlate = plate, Capacity = 2000m, IsActive = true });

            second.IsSuccessStatusCode.Should().BeFalse("biển số trùng phải bị từ chối");
            (await QueryAsync(db => db.Vehicles.CountAsync(v => v.LicensePlate == plate)))
                .Should().Be(1, "không được tạo bản ghi thứ 2");
        }

        /// ADMC-11 | BVA | FT-02 BV-01; BR-006
        /// Bậc chiết khấu áp dụng theo subtotal. Workbook ghi GET /api/admin/discount-tiers/applicable?subtotal=
        /// (không tồn tại) — bậc áp dụng được tính trong checkout-summary, đã kiểm ở L3-ORD-06.
        /// Ở đây kiểm bảng bậc đọc được và biên các bậc đúng cấu hình.
        [Fact]
        public async Task L3_ADMC_11_DiscountTierTable_BoundariesMatchConfiguration()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);

            (await admin.GetAsync("/api/admin/discount-tiers/applicable?subtotal=10000000"))
                .IsSuccessStatusCode.Should().BeFalse("không có endpoint /applicable");

            var res = await admin.GetAsync("/api/admin/discount-tiers");
            res.StatusCode.Should().Be(HttpStatusCode.OK);

            var tiers = await QueryAsync(db => db.DiscountTiers.Where(t => t.IsActive).ToListAsync());
            tiers.Should().NotBeEmpty();
            tiers.Min(t => t.MinAmount).Should().Be(L3Seed.ListPriceMaxExclusive,
                "bậc đầu tiên phải bắt đầu đúng tại ngưỡng 10 triệu");
            tiers.Max(t => t.MaxAmount).Should().Be(L3Seed.QuotationMinValue,
                "bậc cuối phải kết thúc đúng tại ngưỡng 100 triệu");
        }

        /// ADMC-12 | Input-Domain-Error | BR-006; BR-050
        /// Tạo bậc chiết khấu có khoảng CHỒNG LẤN bậc đã có -> bị từ chối, bảng bậc không đổi.
        [Fact]
        public async Task L3_ADMC_12_CreateDiscountTier_OverlappingRange_Rejected()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var before = await QueryAsync(db => db.DiscountTiers.CountAsync());

            // Bậc seed sẵn 10tr–31tr; khoảng dưới đây chồng lấn hoàn toàn.
            var res = await admin.PostAsJsonAsync("/api/admin/discount-tiers", new
            {
                MinAmount = 15_000_000m,
                MaxAmount = 20_000_000m,
                DiscountPercent = 0.5m,
                IsActive = true,
                Description = "Bac chong lan",
            });

            res.IsSuccessStatusCode.Should().BeFalse("khoảng chồng lấn phải bị từ chối");
            (await QueryAsync(db => db.DiscountTiers.CountAsync()))
                .Should().Be(before, "bảng bậc không được đổi");
        }

        /// ADMC-13 | Input-Domain-Error | FT-09 NAC-01; NFR-SEC03
        /// Dashboard Sales Staff luôn lấy phạm vi từ JWT, không nhận staffId từ query
        /// -> không xem được dữ liệu người khác.
        [Fact]
        public async Task L3_ADMC_13_SalesStaffDashboard_ScopeAlwaysFromJwt_NotFromQuery()
        {
            var s1 = await ClientForSeededAsync(L3Seed.SalesStaffId);

            var own = await s1.GetAsync("/api/dashboards/sales-staff");
            var spoofed = await s1.GetAsync($"/api/dashboards/sales-staff?staffId={L3Seed.SalesStaff2Id}");

            own.StatusCode.Should().Be(HttpStatusCode.OK);
            spoofed.StatusCode.Should().Be(HttpStatusCode.OK);

            // So sánh trường phạm vi chứ không so cả thân phản hồi (periodFrom/periodTo là mốc thời
            // gian sinh tại thời điểm gọi nên luôn lệch vài mili giây giữa 2 request).
            var spoofedScope = (await ReadJsonAsync(spoofed))
                .GetProperty("kpi").GetProperty("salesStaffId").GetGuid();
            spoofedScope.Should().Be(L3Seed.SalesStaffId,
                "tham số staffId phải bị bỏ qua hoàn toàn — phạm vi lấy từ JWT");
            spoofedScope.Should().NotBe(L3Seed.SalesStaff2Id, "không được trả dữ liệu của Sales khác");

            var (customer, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customer.GetAsync("/api/dashboards/sales-staff"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        /// ADMC-14 | Input-Domain-Happy | BR-049; FT-09 AC-05
        /// Job chưa chạy KHÔNG được hiển thị là "khoẻ".
        [Fact]
        public async Task L3_ADMC_14_JobHealth_NeverReportsUnrunJobAsHealthy()
        {
            // Summary tổng hợp theo 7 job ĐÃ ĐĂNG KÝ, nên phải seed lần chạy hỏng cho một job có thật.
            const string failedJob = "OrderSla";
            await SeedAsync(db =>
            {
                db.JobRuns.Add(new JobRun
                {
                    Id = Guid.NewGuid(),
                    JobName = failedJob,
                    Status = JobRunStatus.Failed,
                    StartedAt = DateTime.UtcNow.AddMinutes(-10),
                    FinishedAt = DateTime.UtcNow.AddMinutes(-9),
                    ErrorMessage = "Loi ket noi",
                    TriggerType = JobTriggerType.Scheduled,
                });
                return Task.CompletedTask;
            });

            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var res = await admin.GetAsync("/api/admin/system-health/job-runs/summary");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var jobs = (await ReadJsonAsync(res)).EnumerateArray().ToList();

            var failed = jobs.Single(j => j.GetProperty("jobName").GetString() == failedJob);
            failed.GetProperty("todayFailureCount").GetInt32()
                .Should().Be(1, "job thất bại phải được đếm trong báo cáo sức khoẻ");

            // Bất biến chính: job CHƯA CHẠY phải hiện rõ là chưa chạy (lastRun = null) và tuyệt đối
            // không được đếm thành công — tức không bao giờ "im lặng báo khoẻ".
            foreach (var job in jobs.Where(j => j.GetProperty("lastRun").ValueKind == System.Text.Json.JsonValueKind.Null))
            {
                job.GetProperty("todaySuccessCount").GetInt32()
                    .Should().Be(0, $"job {job.GetProperty("jobName").GetString()} chưa chạy thì không được tính là thành công");
            }
        }

        /// ADMC-15 | Idempotency | FT-03 NAC-02; BR-029
        /// Retry webhook log đã Processed -> không tạo PaymentTransaction thứ 2.
        [Fact]
        public async Task L3_ADMC_15_RetryProcessedWebhookLog_Idempotent()
        {
            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var transactionsBefore = await QueryAsync(db => db.PaymentTransactions.CountAsync());

            var res = await admin.PostAsJsonAsync(
                $"/api/admin/system-health/webhook-logs/{Guid.NewGuid()}/retry", new { });

            ((int)res.StatusCode).Should().BeLessThan(500, "phải trả lỗi nghiệp vụ xác định, không phải 500");
            (await QueryAsync(db => db.PaymentTransactions.CountAsync()))
                .Should().Be(transactionsBefore, "retry không được sinh giao dịch thanh toán mới");
        }

        /// ADMC-16 | BVA | FT-09 BV-03; BR-049; NFR-A04
        /// Job thất bại phải để lại bản ghi lỗi — không được thất bại im lặng.
        [Fact]
        public async Task L3_ADMC_16_FailedJob_LeavesFailureRecord_NotSilent()
        {
            await SeedAsync(db =>
            {
                db.JobRuns.Add(new JobRun
                {
                    Id = Guid.NewGuid(),
                    JobName = "JobRetryExhausted",
                    Status = JobRunStatus.Failed,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5),
                    FinishedAt = DateTime.UtcNow.AddMinutes(-4),
                    ErrorMessage = "Vuot qua so lan retry toi da",
                    TriggerType = JobTriggerType.Scheduled,
                });
                return Task.CompletedTask;
            });

            var admin = await ClientForSeededAsync(L3Seed.AdminId);
            var res = await admin.GetAsync("/api/admin/system-health/job-runs");

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            (await res.Content.ReadAsStringAsync())
                .Should().Contain("Vuot qua so lan retry toi da",
                    "thông điệp lỗi phải tra cứu được qua API, không chỉ nằm trong log file");
        }
    }
}
