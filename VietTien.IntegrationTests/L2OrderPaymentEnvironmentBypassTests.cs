using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.DTOs.SePay;
using VietTien.API.Models;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// L2-PAY-10 / L2-PAY-11 — nhánh bypass xác thực webhook theo biến môi trường.
    ///
    /// SePayController.cs:52 và OrderService.cs:300 đọc <c>Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")</c>
    /// — biến TOÀN TIẾN TRÌNH, không phải IHostEnvironment. Khi biến đó = "Development" VÀ token RỖNG
    /// thì việc kiểm token bị bỏ qua hoàn toàn.
    ///
    /// Vì biến này toàn tiến trình, 2 case dưới đây:
    ///   - nằm trong class RIÊNG, và mọi class L2 đều thuộc collection "sqlserver" nên xUnit chạy
    ///     TUẦN TỰ, không song song với các test khác;
    ///   - tự set và KHÔI PHỤC biến trong finally.
    /// </summary>
    [Trait("Category", "L2")]
    public class L2OrderPaymentEnvironmentBypassTests : SqlServerTestBase
    {
        public L2OrderPaymentEnvironmentBypassTests(SqlServerFixture factory) : base(factory) { }

        private const string EnvVar = "ASPNETCORE_ENVIRONMENT";
        private const string WebhookUrl = "/api/webhooks/sepay-callback";

        private string SePayToken =>
            Factory.Services.GetRequiredService<IConfiguration>()["SePaySettings:ApiToken"]!;

        private async Task<(Guid OrderId, string OrderCode, decimal Amount)> SeedSePayOrderAsync(decimal amount)
        {
            var orderId = Guid.NewGuid();
            var orderCode = "VT" + Random.Shared.NextInt64(100_000_000, 999_999_999);

            await SeedAsync(async db =>
            {
                var profileId = await db.CustomerProfiles.Select(c => c.Id).FirstAsync();
                var inventory = await db.Inventories.FirstAsync(i => i.ProductId != null);
                inventory.OnHandQuantity = 10_000;
                inventory.ReservedQuantity = 1_000;
                inventory.AllocatedQuantity = 0;

                db.Orders.Add(new Order
                {
                    Id = orderId,
                    CustomerProfileId = profileId,
                    OrderCode = orderCode,
                    TotalAmount = amount,
                    FinalPayment = amount,
                    PaymentMethod = PaymentMethod.SePay,
                    PaymentStatus = PaymentStatus.Pending,
                    OrderStatus = OrderStatus.Draft,
                    CreatedAt = DateTime.UtcNow,
                    OrderItems = new List<OrderItem>
                    {
                        new() { ProductId = inventory.ProductId!.Value, Quantity = 1, PriceSnapshot = amount, CostSnapshot = 0m }
                    }
                });
            });

            return (orderId, orderCode, amount);
        }

        private static SePayWebhookDto BuildPayload(string orderCode, decimal amount) => new()
        {
            gateway = "TPBank",
            transactionDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            accountNumber = "71111810204",
            transferAmount = amount,
            transferType = "in",
            transferContent = orderCode,
            content = orderCode,
            referenceCode = "FT" + Guid.NewGuid().ToString("N")[..12],
            referenceNumber = "FT" + Guid.NewGuid().ToString("N")[..12]
        };

        // ── L2-PAY-10 ──────────────────────────────────────────────────────────────────────

        // GIVEN  tiến trình đang chạy với ASPNETCORE_ENVIRONMENT=Development và đơn SePay đang Pending
        // WHEN   webhook gửi KHÔNG kèm token
        // THEN   SRS: xác thực webhook không được phụ thuộc môi trường triển khai -> vẫn phải 401,
        //        đơn giữ nguyên Pending, không tạo PaymentTransaction
        [Fact]
        [Trait("TestID", "L2-PAY-10")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "SEC-03")]
        public async Task L2_PAY_10_MissingToken_MustStillBeRejected_EvenWhenEnvironmentIsDevelopment()
        {
            var original = Environment.GetEnvironmentVariable(EnvVar);
            try
            {
                Environment.SetEnvironmentVariable(EnvVar, "Development");

                await ResetAsync();
                var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_100_000m);

                // KHÔNG gắn header x-sepay-token
                var response = await Factory.CreateClient()
                    .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount));

                // (a) HTTP
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "SEC-03: kiểm tra token của webhook thanh toán không được nới lỏng theo biến môi trường");

                // (b) DB
                var order = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
                order.PaymentStatus.Should().Be(PaymentStatus.Pending,
                    "webhook không xác thực được thì không được phép ghi Paid");
                (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                    .Should().Be(0);

                // (c) side effect
                (await QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == orderId)))
                    .Should().Be(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvVar, original);
            }
        }

        // ── L2-PAY-11 ──────────────────────────────────────────────────────────────────────

        // GIVEN  tiến trình KHÔNG chạy ở Development (ASPNETCORE_ENVIRONMENT=Production)
        // WHEN   webhook gửi KHÔNG kèm token
        // THEN   401 "Missing Token"; đơn giữ nguyên Pending; không tạo PaymentTransaction
        [Fact]
        [Trait("TestID", "L2-PAY-11")]
        [Trait("Priority", "P1")]
        [Trait("SRSRef", "SEC-03")]
        public async Task L2_PAY_11_MissingToken_IsRejected_WhenEnvironmentIsNotDevelopment()
        {
            var original = Environment.GetEnvironmentVariable(EnvVar);
            try
            {
                Environment.SetEnvironmentVariable(EnvVar, "Production");

                await ResetAsync();
                var (orderId, orderCode, amount) = await SeedSePayOrderAsync(1_100_000m);

                var response = await Factory.CreateClient()
                    .PostAsJsonAsync(WebhookUrl, BuildPayload(orderCode, amount));

                // (a) HTTP
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                (await response.Content.ReadAsStringAsync()).Should().Contain("Missing Token");

                // (b) DB
                var order = await QueryAsync(db => db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId));
                order.PaymentStatus.Should().Be(PaymentStatus.Pending);
                (await QueryAsync(db => db.PaymentTransactions.CountAsync(t => t.OrderId == orderId)))
                    .Should().Be(0);

                // (c) side effect
                (await QueryAsync(db => db.Notifications.CountAsync(n => n.ReferenceId == orderId)))
                    .Should().Be(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvVar, original);
            }
        }
    }
}
