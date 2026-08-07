using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.ScheduledJobs;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Smoke test cho hạ tầng L2 — chưa phải test nghiệp vụ. Chứng minh: container boot, migrations áp
    /// được lên DB trắng, seed HasData có mặt, JWT thật ký/verify được qua pipeline HTTP thật,
    /// ResetAsync() cô lập đúng (xoá dữ liệu test nhưng giữ seed), job gọi trực tiếp được, và môi trường
    /// "Test" không nạp secret thật.
    ///
    /// Gắn [Trait("Category","L2")] để lọc: `dotnet test --filter "Category!=L2"` chạy 19 test L3 mà
    /// không đụng tới Docker.
    /// </summary>
    [Trait("Category", "L2")]
    public class SqlServerInfraSmokeTests : SqlServerTestBase
    {
        public SqlServerInfraSmokeTests(SqlServerFixture factory) : base(factory) { }

        [Fact]
        public async Task Infra_MigratesSeedsAuthenticatesResetsAndRunsJob()
        {
            await ResetAsync();

            // 1. Migrations đã áp lên DB SQL Server thật (không phải EnsureCreated).
            var applied = await QueryAsync(db => db.Database.GetAppliedMigrationsAsync());
            applied.Should().NotBeEmpty();
            applied.Should().Contain(m => m.EndsWith("_InitData"), "migration gốc (InitData) đã áp lên DB");

            // 2. Seed HasData có mặt sau ResetAsync — SePayReservationExpiryJob và
            //    OrderService.CalculateDiscount phụ thuộc vào hai bảng này.
            (await QueryAsync(db => db.SystemConfigs.CountAsync())).Should().BeGreaterThan(0);
            (await QueryAsync(db => db.DiscountTiers.CountAsync())).Should().BeGreaterThan(0);
            var seededUsers = await QueryAsync(db => db.Users.CountAsync());
            seededUsers.Should().Be(11, "OnModelCreating seed 7 user gốc (111...-777...) + 4 user thêm cho round-robin/kho vệ tinh (seed data warehouse)");

            // 3. Pipeline HTTP thật + JWT thật (IJwtService, không phải TestAuthHandler).
            var (adminClient, adminUser) = await CreateClientAsAsync(SystemRole.Admin);
            var response = await adminClient.GetAsync("/api/admin/audit-logs/export");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "body: {0}", await response.Content.ReadAsStringAsync());

            var (customerClient, _) = await CreateClientAsAsync(SystemRole.Customer);
            (await customerClient.GetAsync("/api/admin/audit-logs/export")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden);

            (await QueryAsync(db => db.Users.CountAsync())).Should().Be(seededUsers + 2);

            // 4. Job chạy trực tiếp được (vòng lặp nền đã bị RemoveAll<IHostedService>() gỡ).
            (await Factory.RunJobAsync<QuotationExpiryJob>()).Should().BeGreaterOrEqualTo(0);
            (await Factory.RunJobAsync<UpcomingDeliveryReminderJob>()).Should().BeGreaterOrEqualTo(0);
            (await Factory.RunJobAsync<OrderSlaJob>()).Should().BeGreaterOrEqualTo(0);

            // 5. ResetAsync() xoá user do test tạo nhưng giữ nguyên seed.
            await ResetAsync();
            (await QueryAsync(db => db.Users.CountAsync())).Should().Be(seededUsers);
            (await QueryAsync(db => db.Users.AnyAsync(u => u.Id == adminUser.Id))).Should().BeFalse();
            (await QueryAsync(db => db.SystemConfigs.CountAsync())).Should().BeGreaterThan(0);

            // Bất biến "không có IO ra ngoài" giờ do teardown của SqlServerTestBase tự kiểm — không
            // cần assert thủ công ở đây nữa.
        }

        /// <summary>
        /// Chốt việc tách môi trường: host chạy ở "Test", KHÔNG nạp appsettings.Development.json,
        /// nên mọi secret thật đều vắng mặt — hàng rào thứ hai nếu có test quên fake.
        /// </summary>
        [Fact]
        public void Environment_IsTest_AndNoRealSecretsAreLoaded()
        {
            using var scope = Factory.Services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var env = Factory.Services.GetRequiredService<IHostEnvironment>();

            env.EnvironmentName.Should().Be("Test");
            env.IsDevelopment().Should().BeFalse("appsettings.Development.json chứa API key thật");

            // appsettings.Test.json phải thắng appsettings.json (nạp sau).
            config["JwtSettings:SecretKey"].Should().StartWith("IntegrationTestsOnly_");

            // Với các key này rỗng: eSmsService.cs:24 rơi vào nhánh mock (không gọi HTTP) và
            // AiGeneratorService.cs:55 bỏ qua nhánh gọi Gemini.
            config["eSMS:ApiKey"].Should().BeNullOrEmpty();
            config["eSMS:SecretKey"].Should().BeNullOrEmpty();
            config["GeminiSettings:ApiKey"].Should().BeNullOrEmpty();
            config["CloudinarySettings:ApiKey"].Should().BeNullOrEmpty();
            config["CloudinarySettings:ApiSecret"].Should().BeNullOrEmpty();
            config["EmailSettings:SenderPassword"].Should().BeNullOrEmpty();
            config["MakeCom:WebhookUrl"].Should().BeNullOrEmpty();

            // SePaySettings:ApiToken là shared secret của webhook ĐI VÀO (không gọi ra ngoài) nên phải
            // có giá trị — nhưng bắt buộc là giá trị giả, không được là token thật của Development.
            config["SePaySettings:ApiToken"].Should().Be("test-sepay-token-not-a-real-secret");

            // Connection string để rỗng có chủ đích: nếu phần ghi đè của fixture hỏng, test phải fail
            // ồn ào thay vì âm thầm trỏ vào DB dev thật rồi bị Respawn xoá sạch.
            config.GetConnectionString("DefaultConnection").Should().BeNullOrEmpty();
        }

        /// <summary>
        /// Chốt yêu cầu "với key rỗng thì nhánh mock ở eSmsService.cs:24 kích hoạt": dựng eSmsService
        /// THẬT với IConfiguration của môi trường Test và một HttpMessageHandler sẽ nổ nếu bị gọi.
        /// Nếu nhánh mock không kích hoạt, handler sẽ ném và test fail.
        /// </summary>
        [Fact]
        public async Task RealESmsService_WithEmptyKeys_ShortCircuitsWithoutAnyHttpCall()
        {
            using var scope = Factory.Services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var handler = new ExplodingHandler();
            var sut = new eSmsService(new HttpClient(handler), config);

            var (success, error) = await sut.SendSmsAsync("0900000000", "noi dung test");

            success.Should().BeTrue();
            error.Should().BeEmpty();
            handler.WasCalled.Should().BeFalse("eSMS:ApiKey/SecretKey rỗng nên phải thoát trước khi gọi HTTP");
        }

        private sealed class ExplodingHandler : HttpMessageHandler
        {
            public bool WasCalled { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                WasCalled = true;
                throw new InvalidOperationException($"Test đã gọi HTTP thật tới {request.RequestUri}");
            }
        }

        /// <summary>
        /// Chứng minh cơ chế opt-out hoạt động: test này CHỦ Ý sinh bản ghi SMS. Nếu bỏ dòng
        /// AllowOutboundSms = true thì teardown của SqlServerTestBase phải làm nó fail.
        /// </summary>
        [Fact]
        public async Task OutboundGuard_CanBeOptedOut_WhenTestIntentionallySendsSms()
        {
            AllowOutboundSms = true;

            var (ok, error) = await Factory.Sms.SendSmsAsync("0900000000", "test");

            ok.Should().BeTrue();
            error.Should().BeEmpty();
            Factory.Sms.Sent.Should().ContainSingle(s => s.PhoneNumber == "0900000000");
        }
    }
}
