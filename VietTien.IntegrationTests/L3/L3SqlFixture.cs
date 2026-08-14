using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Respawn;
using Respawn.Graph;
using VietTien.API.Data;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Host cho toàn bộ test L3 (Report_5_3 — System/API Test).
    ///
    /// VÌ SAO KHÔNG DÙNG <see cref="CustomWebApplicationFactory"/> (EF InMemory):
    /// <c>OrderService.PlaceOrderAsync</c> (OrderService.cs:167) mở transaction thật bằng
    /// <c>_context.Database.BeginTransactionAsync()</c>. Provider InMemory không hỗ trợ transaction và
    /// ném <c>TransactionIgnoredWarning</c> -> mọi case đặt hàng/xuất kho/chuyển kho sẽ hỏng vì lý do
    /// hạ tầng chứ không phải vì nghiệp vụ sai. L3 phải chạy trên stack THẬT.
    ///
    /// VÌ SAO KHÔNG DÙNG <see cref="SqlServerFixture"/> (L2): fixture đó kéo SQL Server qua
    /// Testcontainers, cần Docker daemon. Máy chạy đợt kiểm thử này không bật Docker, nhưng CÓ
    /// SQL Server local (service MSSQLSERVER). Fixture này dùng thẳng SQL Server local, DB RIÊNG
    /// <c>VietTien22_L3</c> — tách khỏi <c>VietTien22</c> (DB mà API local phục vụ Newman/JMeter dùng)
    /// và tách khỏi <c>VietTienDB</c> (DB dev của lập trình viên) để Respawn không bao giờ xoá nhầm.
    ///
    /// Cấu hình lấy từ <c>appsettings.Test.json</c> giống L2 — tức KHÔNG nạp appsettings.Development.json,
    /// nên không có API key thật nào của eSMS/Gemini/Cloudinary/make.com lọt vào tiến trình test.
    /// </summary>
    public class L3SqlFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        /// <summary>DB riêng của L3. KHÔNG trùng VietTien22 (API local) và VietTienDB (DB dev).</summary>
        public string ConnectionString =>
            "Server=localhost;Database=VietTien22_L3;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=30;";

        /// <summary>Secret GIẢ dùng cho header <c>x-make-secret</c> của callback Make.com trong test.</summary>
        public const string MakeCallbackSecret = "l3-make-callback-secret-not-a-real-secret";

        private Respawner _respawner = null!;

        // Fake IO — chặn mọi lệnh gọi ra ngoài và cho phép test assert "đã gửi mail/SMS/webhook gì".
        public FakeEmailService Email { get; } = new();
        public FakeSmsService Sms { get; } = new();
        public FakeMakeWebhookService MakeWebhook { get; } = new();
        public FakeAiGeneratorService Ai { get; } = new();
        public FakeCloudinaryService Cloudinary { get; } = new();

        private static readonly string TestSettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile(TestSettingsPath, optional: false, reloadOnChange: false);

                // PHẢI đứng SAU AddJsonFile: nguồn thêm sau thắng. appsettings.Test.json để
                // MakeCom:CallbackSecret rỗng, mà với environment != "Development"
                // MarketingPostController.cs:209 luôn đòi header x-make-secret rồi so với giá trị đó ->
                // string.IsNullOrEmpty(provided) luôn true -> MỌI callback trả 401 và
                // HandleMakeWebhookCallbackAsync không bao giờ chạy. Đặt secret GIẢ khác rỗng để các
                // case MKT-05/06 kiểm được logic callback thật.
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MakeCom:CallbackSecret"] = MakeCallbackSecret,
                });
            });

            // Program.cs đọc JwtSettings NGAY tại thời điểm builder (trước Build()) rồi capture vào
            // closure của AddJwtBearer -> ConfigureAppConfiguration ở trên chạy quá muộn cho phần đó.
            // UseSetting được nạp ngay từ đầu nên CreateBuilder() thấy được.
            foreach (var kv in new ConfigurationBuilder().AddJsonFile(TestSettingsPath).Build().AsEnumerable())
            {
                if (kv.Value is not null) builder.UseSetting(kv.Key, kv.Value);
            }

            builder.ConfigureServices(services =>
            {
                // Program.cs:32 khai AddHttpsRedirection(HttpsPort = 443) (fix L3-SEC-05) và bật
                // UseHttpsRedirection() cho MỌI environment khác "Development". Fixture này chạy
                // environment "Test" nên middleware đó hoạt động: mỗi request qua TestServer bị 307
                // sang https://localhost/... , HttpClient tự đi theo redirect và theo đúng đặc tả
                // của .NET nó GỠ BỎ header Authorization khi redirect đổi scheme/origin. Kết quả là
                // mọi call cần đăng nhập trả 401 -> 126/199 case đỏ oan.
                //
                // Đặt HttpsPort = null: middleware không phân giải được cổng HTTPS nên chỉ ghi log
                // cảnh báo rồi cho request đi tiếp, không redirect. Chỉ ảnh hưởng host test.
                // KHÔNG đổi environment sang "Development" — "Test" là có chủ đích, để tránh nạp
                // appsettings.Development.json vốn chứa API key THẬT.
                // Bản thân hành vi redirect (L3-SEC-05) vẫn được kiểm riêng bằng Newman/curl đánh
                // vào server thật, nên bỏ nó ở đây không làm mất độ phủ.
                services.PostConfigure<HttpsRedirectionOptions>(o => o.HttpsPort = null);

                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));

                // Gỡ vòng lặp job nền: L3 kiểm hợp đồng HTTP, không đợi scheduler. Các job vẫn resolve
                // được qua IScheduledJob nếu case nào cần chạy tay (xem RunJobAsync).
                services.RemoveAll<IHostedService>();

                ReplaceSingleton<IEmailService>(services, Email);
                ReplaceSingleton<ISmsService>(services, Sms);
                ReplaceSingleton<IMakeWebhookService>(services, MakeWebhook);
                ReplaceSingleton<IAiGeneratorService>(services, Ai);
                ReplaceSingleton<ICloudinaryService>(services, Cloudinary);
            });
        }

        private static void ReplaceSingleton<TService>(IServiceCollection services, TService instance)
            where TService : class
        {
            services.RemoveAll<TService>();
            services.AddSingleton(instance);
        }

        public async Task InitializeAsync()
        {
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
            }

            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
            });
        }

        /// <summary>Đưa DB về đúng trạng thái "vừa migrate xong" rồi nạp lại toàn bộ seed HasData.</summary>
        public async Task ResetAsync()
        {
            await using (var conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();

                // Migration 20260813035338_AddAuditLogInsertOnlyTrigger dựng trigger INSTEAD OF
                // UPDATE/DELETE trên AuditLogs (BR-048/NFR-SEC08). Trigger đó chặn luôn DELETE của
                // Respawn -> ResetAsync ném lỗi và MỌI test chết ở InitializeAsync.
                //
                // KHÔNG thêm AuditLogs vào TablesToIgnore: làm vậy thì bảng không bao giờ được dọn,
                // audit log của test trước rơi sang test sau và mọi assertion đếm số bản ghi audit
                // sẽ đỏ giả. Thay vào đó tắt trigger ĐÚNG trong lúc dọn rồi bật lại ngay — thân test
                // vẫn chạy với bất biến có hiệu lực đầy đủ.
                await SetAuditLogTriggerAsync(conn, enabled: false);
                try
                {
                    await _respawner.ResetAsync(conn);
                }
                finally
                {
                    await SetAuditLogTriggerAsync(conn, enabled: true);
                }
            }

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedDataReplayer.ReseedAsync(db);
        }

        /// <summary>
        /// Bật/tắt trigger bất biến của AuditLogs. Bọc IF EXISTS để vẫn chạy được trên DB dựng từ
        /// migration cũ (chưa có trigger) — khi đó câu lệnh không làm gì.
        /// </summary>
        private static async Task SetAuditLogTriggerAsync(SqlConnection conn, bool enabled)
        {
            var verb = enabled ? "ENABLE" : "DISABLE";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_AuditLogs_PreventUpdateDelete')
                    ALTER TABLE dbo.AuditLogs {verb} TRIGGER trg_AuditLogs_PreventUpdateDelete;";
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Chạy trực tiếp 1 scheduled job (vòng lặp nền đã bị gỡ ở ConfigureServices).</summary>
        public async Task<int> RunJobAsync<TJob>(CancellationToken ct = default) where TJob : IScheduledJob
        {
            using var scope = Services.CreateScope();
            var job = scope.ServiceProvider.GetServices<IScheduledJob>().OfType<TJob>().Single();
            return await job.RunAsync(ct);
        }

        public void ClearOutboundRecords()
        {
            Email.Sent.Clear();
            Sms.Sent.Clear();
            MakeWebhook.Triggered.Clear();
            Ai.Requests.Clear();
            Cloudinary.Uploaded.Clear();
        }

        public async Task ExecuteDbAsync(Func<ApplicationDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await action(db);
        }

        // WebApplicationFactory<T> đã có ValueTask DisposeAsync() (IAsyncDisposable) còn xUnit
        // IAsyncLifetime đòi Task DisposeAsync() — trùng tên khác kiểu trả về nên phải explicit.
        async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
    }

    /// <summary>
    /// Một host + một DB dùng chung cho toàn bộ class test L3. Migrate/tạo DB chỉ chạy 1 lần.
    /// Test L3 chạy TUẦN TỰ trong collection này (xUnit không chạy song song trong cùng collection) —
    /// bắt buộc, vì tất cả dùng chung 1 DB vật lý và mỗi test gọi Respawn.
    /// </summary>
    [CollectionDefinition(Name)]
    public class L3Collection : ICollectionFixture<L3SqlFixture>
    {
        public const string Name = "l3-sqlserver";
    }
}
