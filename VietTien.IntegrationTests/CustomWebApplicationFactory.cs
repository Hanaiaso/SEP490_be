using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using VietTien.API.Data;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Host L3 dùng cho Phase 3 (case Blocked vì phân quyền nằm ở [Authorize] Controller, không
    /// unit-test được ở tầng service — xem FIX_PLAN_2026-08-03.md). Chạy pipeline HTTP thật (JWT
    /// Bearer + Authorization middleware thật) nhưng thay DbContext SQL Server bằng EF InMemory
    /// (không dùng SQLite: provider SQLite của EFCore không dịch được Sum() trên cột decimal —
    /// nhiều service dashboard/KPI dùng Sum(decimal), InMemory chạy LINQ phía client nên không vướng).
    /// </summary>
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"IntegrationTests_{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Dùng appsettings.Development.json (có JwtSettings:SecretKey thật) thay vì môi trường
            // mặc định "Production" của WebApplicationFactory — nếu không JWT sẽ không ký/verify được.
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_dbName);
                });

                // Job chạy nền mỗi phút + chạy ngay khi start -> gây nhiễu/không cần thiết cho L3
                // test (vốn chỉ kiểm tra hành vi [Authorize], không kiểm tra scheduled job).
                services.RemoveAll<IHostedService>();

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.EnsureCreated();
            });
        }
    }
}
