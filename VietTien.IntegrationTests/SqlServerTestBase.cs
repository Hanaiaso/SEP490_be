using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Base cho test L2 (SQL Server thật). Tái sử dụng NGUYÊN logic CreateClientAsAsync/SeedAsync của
    /// <see cref="IntegrationTestBase"/> — vẫn mint JWT bằng IJwtService thật resolve từ container
    /// (không tự chế token, không TestAuthHandler) — chỉ đổi nguồn fixture từ CustomWebApplicationFactory
    /// sang <see cref="SqlServerFixture"/>.
    ///
    /// Không kế thừa IntegrationTestBase vì class đó ràng IClassFixture&lt;CustomWebApplicationFactory&gt;,
    /// tức là sẽ kéo theo cả host EF InMemory thứ hai cho mỗi test class L2.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    public abstract class SqlServerTestBase : IAsyncLifetime
    {
        protected readonly SqlServerFixture Factory;

        protected SqlServerTestBase(SqlServerFixture factory)
        {
            Factory = factory;
        }

        // ---------------------------------------------------------------------------------------
        // Bất biến toàn cục: KHÔNG test L2 nào được phép gọi IO ra ngoài.
        //
        // Trước đây chỉ SqlServerInfraSmokeTests tự assert — đó là hàng rào thủ công, test sau này
        // quên là lọt. Giờ mọi test kế thừa class này đều bị kiểm tự động ở teardown.
        //
        // Email CỐ TÌNH không nằm trong danh sách: nhiều luồng nghiệp vụ (đặt hàng, chuyển kho) gửi mail
        // hợp lệ. Dùng Factory.Email.Sent để tự assert nội dung thay vì assert rỗng.
        // ---------------------------------------------------------------------------------------

        /// <summary>Đặt true khi test CHỦ Ý kiểm luồng gửi SMS (vd xác thực SĐT) — teardown sẽ bỏ qua.</summary>
        protected bool AllowOutboundSms { get; set; } = false;

        /// <summary>Đặt true khi test chủ ý kiểm luồng đẩy bài lên Make.com.</summary>
        protected bool AllowOutboundMakeWebhook { get; set; } = false;

        /// <summary>Đặt true khi test chủ ý kiểm luồng sinh nội dung AI.</summary>
        protected bool AllowOutboundAi { get; set; } = false;

        /// <summary>Đặt true khi test chủ ý kiểm luồng upload ảnh.</summary>
        protected bool AllowOutboundCloudinary { get; set; } = false;

        Task IAsyncLifetime.InitializeAsync()
        {
            // Fake là singleton dùng chung cả collection -> phải xoá dấu vết test trước, nếu không
            // test thứ hai sẽ fail vì rác của test thứ nhất.
            Factory.ClearOutboundRecords();
            return SetUpAsync();
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            try
            {
                await TearDownAsync();
            }
            finally
            {
                AssertNoUnexpectedOutboundIo();
            }
        }

        /// <summary>Hook cho lớp con thay cho InitializeAsync (đã bị niêm để bất biến không bị override mất).</summary>
        protected virtual Task SetUpAsync() => Task.CompletedTask;

        /// <summary>Hook cho lớp con thay cho DisposeAsync.</summary>
        protected virtual Task TearDownAsync() => Task.CompletedTask;

        private void AssertNoUnexpectedOutboundIo()
        {
            var violations = new List<string>();

            if (!AllowOutboundSms && !Factory.Sms.Sent.IsEmpty)
            {
                violations.Add($"SMS ({Factory.Sms.Sent.Count} lần): " +
                               string.Join("; ", Factory.Sms.Sent.Select(s => s.PhoneNumber)));
            }

            if (!AllowOutboundMakeWebhook && !Factory.MakeWebhook.Triggered.IsEmpty)
            {
                violations.Add($"Make.com webhook ({Factory.MakeWebhook.Triggered.Count} lần)");
            }

            if (!AllowOutboundAi && !Factory.Ai.Requests.IsEmpty)
            {
                violations.Add($"AI generator ({Factory.Ai.Requests.Count} lần)");
            }

            if (!AllowOutboundCloudinary && !Factory.Cloudinary.Uploaded.IsEmpty)
            {
                violations.Add($"Cloudinary upload ({Factory.Cloudinary.Uploaded.Count} lần)");
            }

            if (violations.Count == 0) return;

            throw new InvalidOperationException(
                "Test đã kích hoạt IO ra ngoài mà không khai báo: " + string.Join(" | ", violations) +
                ". Nếu đúng là chủ ý, đặt cờ AllowOutbound* tương ứng = true trong test. " +
                "Nếu không, đây là dấu hiệu luồng nghiệp vụ gọi service ngoài ngoài dự kiến.");
        }

        /// <summary>Tạo (và lưu) 1 User với role chỉ định, trả về HttpClient đã gắn sẵn Bearer token của user đó.</summary>
        protected async Task<(HttpClient client, User user)> CreateClientAsAsync(SystemRole role, Action<User>? mutate = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = $"Test {role}",
                Email = $"{role}.{Guid.NewGuid():N}@test.local",
                PhoneNumber = "09" + Random.Shared.Next(0, 100_000_000).ToString("D8"),
                PasswordHash = "x",
                Role = role,
                IsActive = true,
                IsEmailVerified = true,
            };
            mutate?.Invoke(user);

            string token;
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Users.Add(user);
                await db.SaveChangesAsync();

                var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
                token = jwt.GenerateAccessToken(user);
            }

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, user);
        }

        /// <summary>Chạy 1 thao tác trên ApplicationDbContext trong 1 scope riêng (để seed dữ liệu test).</summary>
        protected async Task SeedAsync(Func<ApplicationDbContext, Task> seed)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await seed(db);
            await db.SaveChangesAsync();
        }

        /// <summary>Đọc dữ liệu từ DB trong scope riêng (DbContext sạch, không dính ChangeTracker của test).</summary>
        protected async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await query(db);
        }

        /// <summary>Xoá sạch DB rồi nạp lại seed HasData — gọi ở đầu test cần cô lập dữ liệu.</summary>
        protected Task ResetAsync() => Factory.ResetAsync();
    }
}
