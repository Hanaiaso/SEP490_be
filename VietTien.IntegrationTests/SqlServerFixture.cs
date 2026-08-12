using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure; // AccessorExtensions.GetService<T>()
using Microsoft.EntityFrameworkCore.Metadata;       // IDesignTimeModel
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Respawn;
using Respawn.Graph;
using Testcontainers.MsSql;
using VietTien.API.Data;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Host L2: chạy trên SQL Server THẬT (Testcontainers) để kiểm những thứ EF InMemory không làm được —
    /// transaction boundary, atomic rollback, idempotency, concurrency token (RowVersion), unique index,
    /// và hành vi thật của 7 scheduled job.
    ///
    /// SONG SONG với hạ tầng L3 (<see cref="CustomWebApplicationFactory"/> + EF InMemory), KHÔNG thay thế:
    /// L3 vẫn phải giữ tốc độ và không kéo Docker vào. Fixture này chỉ được khởi tạo khi có test thuộc
    /// collection "sqlserver" thực sự chạy — xem <see cref="SqlServerCollection"/>.
    ///
    /// Khác biệt CỐ Ý so với CustomWebApplicationFactory:
    ///  - Dùng ICollectionFixture (1 container cho cả suite) thay vì IClassFixture (1 factory mỗi class).
    ///  - KHÔNG gọi services.BuildServiceProvider() trong ConfigureServices. Với SQL Server việc đó tạo
    ///    một đồ thị singleton THỨ HAI (connection pool / EF internal service provider riêng) và gây lỗi
    ///    rất khó truy. Migration được áp ở InitializeAsync() SAU khi host đã build, qua Services.CreateScope().
    /// </summary>
    public class SqlServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private const string TestDatabaseName = "VietTienTests";

        private readonly MsSqlContainer _container =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        private Respawner _respawner = null!;

        /// <summary>Connection string trỏ vào DB test (Testcontainers mặc định trỏ master).</summary>
        public string ConnectionString =>
            new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                InitialCatalog = TestDatabaseName
            }.ConnectionString;

        // Fake IO — expose để test assert được "đã gửi mail/SMS gì". Xem TestDoubles.cs.
        public FakeEmailService Email { get; } = new();
        public FakeSmsService Sms { get; } = new();
        public FakeMakeWebhookService MakeWebhook { get; } = new();
        public FakeAiGeneratorService Ai { get; } = new();
        public FakeCloudinaryService Cloudinary { get; } = new();

        /// <summary>Tên file config riêng của L2, copy sang thư mục output của project test.</summary>
        public static readonly string TestSettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // "Test" chứ KHÔNG phải "Development" (CustomWebApplicationFactory của L3 vẫn giữ "Development").
            // Mục đích: không nạp appsettings.Development.json — file đó (gitignored) chứa API key THẬT của
            // eSMS/Gemini/Cloudinary, SMTP password thật, URL webhook make.com thật. Một test tương lai
            // quên fake là gọi IO thật ra ngoài.
            //
            // Phụ phẩm của việc đổi environment (đã kiểm, đều vô hại):
            //  - Program.cs:261 Swagger tắt.
            //  - Program.cs:271 UseHttpsRedirection() bật, nhưng TestServer không có địa chỉ https nên
            //    HttpsRedirectionMiddleware không xác định được port -> log warning rồi cho qua.
            //  - SePayController.cs:52, OrderService.cs:300, MarketingPostController.cs:186 đọc biến môi
            //    trường TIẾN TRÌNH ASPNETCORE_ENVIRONMENT chứ không đọc IHostEnvironment, nên UseEnvironment()
            //    chưa bao giờ ảnh hưởng tới chúng — hành vi không đổi so với trước.
            builder.UseEnvironment("Test");

            // Content root của WebApplicationFactory là thư mục VietTien.API, nên appsettings.Test.json
            // (nằm ở output của project test) phải nạp bằng đường dẫn tuyệt đối. Delegate này chạy SAU
            // các nguồn config mặc định nên ghi đè được appsettings.json.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile(TestSettingsPath, optional: false, reloadOnChange: false);
            });

            // ConfigureAppConfiguration ở trên chạy QUÁ MUỘN cho một phần của Program.cs: dòng 101-102
            // đọc JwtSettings ngay tại thời điểm builder (trước builder.Build()), rồi capture vào closure
            // của AddJwtBearer. Closure đó chạy lazily ở request đầu tiên -> SecretKey rỗng sẽ thành
            // `new SymmetricSecurityKey(new byte[0])` -> ArgumentException cho MỌI request.
            // UseSetting thì được nạp ngay từ đầu nên WebApplication.CreateBuilder() thấy được.
            foreach (var kv in new ConfigurationBuilder().AddJsonFile(TestSettingsPath).Build().AsEnumerable())
            {
                if (kv.Value is not null) builder.UseSetting(kv.Key, kv.Value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();

                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));

                // Chỉ ScheduledJobRunnerBackgroundService là IHostedService (Program.cs:253). 7 job đăng ký
                // AddScoped<IScheduledJob, X> (Program.cs:83-89) nên VẪN resolve được — test gọi thẳng
                // qua RunJobAsync<TJob>() thay vì chờ vòng lặp nền.
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
            // Container phải chạy TRƯỚC khi chạm Services — ConnectionString đọc port do Docker cấp,
            // và chạm Services là ép host build (kéo theo ConfigureWebHost).
            await _container.StartAsync();

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
            }

            // Respawn 7 chỉ còn overload nhận DbConnection (bản string đã bị bỏ).
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
            });
        }

        /// <summary>
        /// Đưa DB về đúng trạng thái "vừa migrate xong": xoá sạch mọi bảng (trừ __EFMigrationsHistory)
        /// rồi nạp lại toàn bộ seed HasData. Gọi ở đầu mỗi test cần cô lập dữ liệu.
        /// </summary>
        public async Task ResetAsync()
        {
            await using (var conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();
                await _respawner.ResetAsync(conn);
            }

            await ReseedAsync();
        }

        /// <summary>
        /// Nạp lại seed data khai báo bằng HasData trong ApplicationDbContext.OnModelCreating
        /// (12 khối: 7 User GUID cố định, CustomerProfile, Warehouse/Location/Shift, Category, Product,
        /// Inventory, SystemConfig + SystemConfigVersion, Vehicle, DiscountTier).
        ///
        /// Đọc thẳng từ model nên tự đồng bộ khi team sửa HasData — không phải chép tay lần hai.
        /// Respawn xoá sạch mọi bảng, mà SePayReservationExpiryJob đọc SystemConfig và
        /// OrderService.CalculateDiscount đọc DiscountTier, nên thiếu bước này là test hỏng.
        ///
        /// ⚠ Chỉ đúng khi entity có seed dùng PK gán tường minh (hiện tại toàn bộ là Guid). Nếu về sau
        /// team seed entity có PK int IDENTITY thì phải chuyển sang raw INSERT + SET IDENTITY_INSERT.
        /// </summary>
        private async Task ReseedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Thân hàm đã tách sang SeedDataReplayer để hạ tầng L3 (L3SqlFixture — SQL Server local,
            // không Docker) dùng lại đúng logic này thay vì chép lần hai. Hành vi không đổi.
            await SeedDataReplayer.ReseedAsync(db);
        }

        /// <summary>
        /// Chạy trực tiếp 1 scheduled job (dự án KHÔNG dùng Hangfire; vòng lặp nền đã bị
        /// RemoveAll&lt;IHostedService&gt;() gỡ). Trả về số item job đã xử lý — xem IScheduledJob.RunAsync.
        ///
        /// Lưu ý: 7 job chỉ đăng ký dưới service type IScheduledJob, KHÔNG đăng ký concrete type,
        /// nên phải lọc bằng OfType&lt;TJob&gt;() chứ không GetRequiredService&lt;TJob&gt;().
        /// </summary>
        public async Task<int> RunJobAsync<TJob>(CancellationToken ct = default) where TJob : IScheduledJob
        {
            using var scope = Services.CreateScope();
            var job = scope.ServiceProvider.GetServices<IScheduledJob>().OfType<TJob>().Single();
            return await job.RunAsync(ct);
        }

        /// <summary>
        /// Xoá sạch bản ghi của cả 5 fake IO. SqlServerTestBase gọi TRƯỚC mỗi test để bất biến
        /// "không có IO ra ngoài" được kiểm theo từng test, không bị lẫn dấu vết của test trước
        /// (fake là singleton dùng chung cả collection).
        /// </summary>
        public void ClearOutboundRecords()
        {
            Email.Sent.Clear();
            Sms.Sent.Clear();
            MakeWebhook.Triggered.Clear();
            Ai.Requests.Clear();
            Cloudinary.Uploaded.Clear();
        }

        /// <summary>Chạy 1 thao tác trên ApplicationDbContext trong scope riêng.</summary>
        public async Task ExecuteDbAsync(Func<ApplicationDbContext, Task> action)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await action(db);
        }

        // WebApplicationFactory<T> đã có `public virtual ValueTask DisposeAsync()` (IAsyncDisposable),
        // còn xUnit IAsyncLifetime đòi `Task DisposeAsync()` — trùng tên, khác kiểu trả về nên C# không
        // cho khai báo cả hai dạng public. Bắt buộc explicit interface implementation.
        async Task IAsyncLifetime.DisposeAsync()
        {
            await base.DisposeAsync();
            await _container.DisposeAsync();
        }
    }
}
