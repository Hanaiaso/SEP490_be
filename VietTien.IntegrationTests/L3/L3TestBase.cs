using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using FluentAssertions;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.IntegrationTests.L3
{
    /// <summary>
    /// Base cho toàn bộ test L3 (System/API — Report_5_3 v1.3).
    ///
    /// Mỗi test bắt đầu trên DB SẠCH: <see cref="InitializeAsync"/> gọi Respawn rồi nạp lại seed
    /// HasData. Vì thế mọi test dùng chung được các hằng ID trong <see cref="L3Seed"/>.
    ///
    /// Host: <see cref="L3SqlFixture"/> — ASP.NET Core thật + SQL Server local, pipeline HTTP + JWT +
    /// middleware Authorization thật, transaction thật.
    /// </summary>
    [Collection(L3Collection.Name)]
    public abstract class L3TestBase : IAsyncLifetime
    {
        protected readonly L3SqlFixture Factory;

        protected L3TestBase(L3SqlFixture factory) => Factory = factory;

        public async Task InitializeAsync()
        {
            await Factory.ResetAsync();
            Factory.ClearOutboundRecords();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ── Sinh dữ liệu định danh không trùng ────────────────────────────────────────────────

        protected static string NewEmail() => $"l3.{Guid.NewGuid():N}@viettien.test";

        protected static string NewPhone() => "09" + Random.Shared.Next(0, 100_000_000).ToString("D8");

        // ── Client + JWT ──────────────────────────────────────────────────────────────────────

        /// <summary>HttpClient KHÔNG kèm token (dùng cho endpoint public và case 401).</summary>
        protected HttpClient AnonymousClient() => Factory.CreateClient();

        /// <summary>Tạo mới một User với vai trò chỉ định và trả HttpClient đã gắn Bearer token.</summary>
        protected async Task<(HttpClient client, User user)> CreateClientAsAsync(
            SystemRole role, Action<User>? mutate = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = $"L3 {role}",
                Email = $"{role}.{Guid.NewGuid():N}@viettien.test",
                PhoneNumber = NewPhone(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(L3Seed.DefaultPassword),
                Role = role,
                IsActive = true,
                IsEmailVerified = true,
                IsPhoneVerified = true,
            };
            mutate?.Invoke(user);

            await SeedAsync(db => { db.Users.Add(user); return Task.CompletedTask; });

            return (ClientFor(user), user);
        }

        /// <summary>HttpClient mang token của một user ĐÃ có trong DB (vd tài khoản seed sẵn).</summary>
        protected async Task<HttpClient> ClientForSeededAsync(Guid userId)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            return ClientFor(user);
        }

        protected HttpClient ClientFor(User user)
        {
            using var scope = Factory.Services.CreateScope();
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
            var token = jwt.GenerateAccessToken(user);

            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>
        /// JWT ĐÃ HẾT HẠN, ký bằng đúng khoá/issuer/audience của host — chứng minh 401 đến từ việc
        /// token quá hạn chứ không phải chữ ký sai. Dùng cho AUTH-13.
        /// </summary>
        protected string CreateExpiredJwt() => CreateJwt(expired: true, signingKeyOverride: null);

        /// <summary>JWT ký bằng khoá của kẻ tấn công — chữ ký không verify được. Dùng cho SEC-09.</summary>
        protected string CreateForgedJwt() =>
            CreateJwt(expired: false, signingKeyOverride: "AttackerControlledKey_NotTheServerSecret_0123456789ABCDEF");

        private string CreateJwt(bool expired, string? signingKeyOverride)
        {
            using var scope = Factory.Services.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var key = signingKeyOverride ?? cfg["JwtSettings:SecretKey"]!;
            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: cfg["JwtSettings:Issuer"],
                audience: cfg["JwtSettings:Audience"],
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, L3Seed.CustomerId.ToString()),
                    new Claim(ClaimTypes.Role, nameof(SystemRole.Customer)),
                },
                notBefore: expired ? now.AddHours(-2) : now,
                expires: expired ? now.AddHours(-1) : now.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ── Đọc phản hồi ──────────────────────────────────────────────────────────────────────

        /// <summary>Đọc trường <c>message</c> — envelope lỗi chuẩn của mọi controller trong dự án.</summary>
        protected static async Task<string> ReadMessageAsync(HttpResponseMessage res)
        {
            var raw = await res.Content.ReadAsStringAsync();
            try
            {
                var root = JsonDocument.Parse(raw).RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m))
                    return m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : m.ToString();
            }
            catch (JsonException) { /* không phải JSON -> trả raw */ }
            return raw;
        }

        protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
        {
            var raw = await res.Content.ReadAsStringAsync();
            return JsonDocument.Parse(raw).RootElement.Clone();
        }

        /// <summary>Response có trường errorCode/error_code/code ở cấp cao nhất không? (bằng chứng DEF-L3-001)</summary>
        protected static async Task<bool> HasErrorCodeFieldAsync(HttpResponseMessage res)
        {
            var raw = await res.Content.ReadAsStringAsync();
            try
            {
                var root = JsonDocument.Parse(raw).RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                return root.TryGetProperty("errorCode", out _)
                    || root.TryGetProperty("error_code", out _)
                    || root.TryGetProperty("code", out _);
            }
            catch (JsonException) { return false; }
        }

        // ── Truy cập DB ───────────────────────────────────────────────────────────────────────

        protected async Task SeedAsync(Func<ApplicationDbContext, Task> seed)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await seed(db);
            await db.SaveChangesAsync();
        }

        protected async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await query(db);
        }

        // ── Seed nghiệp vụ ────────────────────────────────────────────────────────────────────

        /// <summary>Khách đã xác minh email, đăng nhập được bằng <paramref name="password"/>, kèm CustomerProfile.</summary>
        protected async Task<(User user, CustomerProfile profile)> SeedVerifiedCustomerAsync(
            string email, string password, string? taxCode = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = "L3 Verified Customer",
                Email = email,
                PhoneNumber = NewPhone(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = SystemRole.Customer,
                IsActive = true,
                IsEmailVerified = true,
                IsPhoneVerified = true,
            };
            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = user.Id, TaxCode = taxCode };

            await SeedAsync(db =>
            {
                db.Users.Add(user);
                db.CustomerProfiles.Add(profile);
                return Task.CompletedTask;
            });

            return (user, profile);
        }

        /// <summary>Lấy CustomerProfile của user, tạo mới nếu chưa có.</summary>
        protected async Task<CustomerProfile> EnsureProfileAsync(Guid userId)
        {
            var existing = await QueryAsync(db => db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId));
            if (existing != null) return existing;

            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = userId };
            await SeedAsync(db => { db.CustomerProfiles.Add(profile); return Task.CompletedTask; });
            return profile;
        }

        /// <summary>Địa chỉ giao hàng mặc định cho hồ sơ khách (PlaceOrder cần có để chốt snapshot).</summary>
        protected async Task<Address> SeedAddressAsync(Guid customerProfileId, bool isDefault = true)
        {
            var address = new Address
            {
                Id = Guid.NewGuid(),
                CustomerProfileId = customerProfileId,
                ReceiverName = "Nguoi Nhan L3",
                ReceiverPhone = NewPhone(),
                SpecificAddress = "So 1 Duong Test",
                Ward = "Phuong 1",
                District = "Quan 1",
                City = "TP HCM",
                IsDefault = isDefault,
            };
            await SeedAsync(db => { db.Addresses.Add(address); return Task.CompletedTask; });
            return address;
        }

        /// <summary>
        /// Sản phẩm mới + tồn kho tại WH-DEFAULT (kho mà OrderService hard-code theo mã "WH-DEFAULT").
        /// Dùng khi case cần kiểm soát chính xác giá/số lượng thay vì dựa vào sản phẩm seed sẵn.
        /// </summary>
        protected async Task<(Product product, Inventory inventory)> SeedSellableProductAsync(
            decimal unitPrice, int onHandQuantity, Guid? locationId = null)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = Guid.Parse("d373bbfa-184c-4eac-9633-38bee5ef6478"), // Category "Băng Keo" đã seed
                Name = "L3 Product " + Guid.NewGuid().ToString("N")[..6],
                Sku = "L3-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                StandardListedPrice = unitPrice,
                Description = "seed cho test L3",
                Unit = "Cái",
                IsDiscontinued = false,
            };
            var inventory = new Inventory
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                WarehouseLocationId = locationId ?? L3Seed.LocationDefaultId,
                OnHandQuantity = onHandQuantity,
                ReservedQuantity = 0,
                AllocatedQuantity = 0,
                QuarantineQuantity = 0,
                DamagedQuantity = 0,
                InTransitQuantity = 0,
            };

            await SeedAsync(db =>
            {
                db.Products.Add(product);
                db.Inventories.Add(inventory);
                return Task.CompletedTask;
            });

            return (product, inventory);
        }

        /// <summary>
        /// Ghi thẳng giỏ hàng vào DB. <paramref name="cartUpdatedAt"/> chính là "tuổi snapshot giá":
        /// OrderService.cs:153 chặn đơn khi <c>(UtcNow - Cart.UpdatedAt).TotalHours &gt; 24</c>.
        /// </summary>
        protected async Task<Cart> SeedCartAsync(
            Guid customerProfileId,
            DateTime? cartUpdatedAt = null,
            params (Guid productId, int quantity, decimal unitPrice)[] lines)
        {
            var cartId = Guid.NewGuid();

            await SeedAsync(async db =>
            {
                var existing = await db.Carts.FirstOrDefaultAsync(c => c.CustomerProfileId == customerProfileId);
                if (existing != null)
                {
                    db.CartItems.RemoveRange(db.CartItems.Where(i => i.CartId == existing.Id));
                    existing.UpdatedAt = cartUpdatedAt ?? DateTime.UtcNow;
                    cartId = existing.Id;
                }
                else
                {
                    db.Carts.Add(new Cart
                    {
                        Id = cartId,
                        CustomerProfileId = customerProfileId,
                        CreatedAt = cartUpdatedAt ?? DateTime.UtcNow,
                        UpdatedAt = cartUpdatedAt ?? DateTime.UtcNow,
                    });
                }

                foreach (var (productId, quantity, unitPrice) in lines)
                {
                    db.CartItems.Add(new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = cartId,
                        ProductId = productId,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                    });
                }
            });

            return (await QueryAsync(db => db.Carts.SingleAsync(c => c.Id == cartId)));
        }

        /// <summary>
        /// Đặt lại <c>Cart.UpdatedAt</c> SAU khi đã thêm dòng giỏ — cần vì CartService tự làm mới
        /// UpdatedAt mỗi lần ghi. Dùng cho BVA tuổi snapshot (ORD-02/ORD-03).
        /// </summary>
        /// <summary>
        /// VAT 10% là BẮT BUỘC trên mọi đơn kể từ nhánh "VAT bắt buộc, hóa đơn đỏ" (OrderService.cs:213,
        /// 282, 803) — server tự tính, không tin số client gửi. Số tiền phải trả cuối cùng của một đơn
        /// vì thế luôn là (thành tiền - chiết khấu) + VAT. Dùng helper này thay vì hard-code để test
        /// nói đúng ý định nghiệp vụ chứ không phải một con số.
        /// </summary>
        protected const decimal VatRate = 0.10m;

        /// <summary>Tổng phải trả sau khi cộng VAT 10% bắt buộc (làm tròn giống OrderService).</summary>
        protected static decimal WithVat(decimal netAmount) =>
            netAmount + Math.Round(netAmount * VatRate, 0, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Giao báo giá cho một Sales Staff. Nhánh "Báo giá ≥100tr: Sales Manager phân công thủ công"
        /// đã BỎ việc Sale tự nhận (POST /api/Quotation/{id}/pickup nay luôn ném lỗi, QuotationService.cs:140)
        /// — mọi báo giá đều ≥ 100tr nên phải qua POST /api/Quotation/{id}/assign của Sales Manager.
        /// </summary>
        protected async Task AssignQuotationToSalesAsync(Guid quotationId, Guid? staffId = null)
        {
            var manager = await ClientForSeededAsync(L3Seed.SalesManagerId);
            var res = await manager.PostAsJsonAsync($"/api/Quotation/{quotationId}/assign",
                new { StaffId = staffId ?? L3Seed.SalesStaffId });
            res.IsSuccessStatusCode.Should().BeTrue(
                "Sales Manager phải phân công được báo giá cho nhân viên; body: {0}",
                await res.Content.ReadAsStringAsync());
        }

        protected async Task SetCartAgeAsync(Guid cartId, TimeSpan age)
        {
            await SeedAsync(async db =>
            {
                var cart = await db.Carts.SingleAsync(c => c.Id == cartId);
                cart.UpdatedAt = DateTime.UtcNow - age;
            });
        }
    }
}
