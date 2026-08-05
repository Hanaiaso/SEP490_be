using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Fixture dùng chung cho các test Phase 3 (role-gate ở [Authorize] Controller — L1-ORD-47/53/71,
    /// L1-QUO-09, L1-AUD-07, L1-MKT-03, L1-DASH-02/05). Mỗi test class implement IClassFixture&lt;CustomWebApplicationFactory&gt;
    /// riêng nhưng dùng chung các helper ở đây qua kế thừa.
    /// </summary>
    public abstract class IntegrationTestBase : IClassFixture<CustomWebApplicationFactory>
    {
        protected readonly CustomWebApplicationFactory Factory;

        protected IntegrationTestBase(CustomWebApplicationFactory factory)
        {
            Factory = factory;
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
    }
}
