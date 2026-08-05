using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VietTien.API.Services.Interfaces;
using Xunit;

namespace VietTien.IntegrationTests
{
    /// <summary>
    /// Xác nhận toàn bộ service/job vừa được sửa để gọi INotificationService (StockTransferService,
    /// SalesChangeRequestService, MarketingPostService, AdminUserService, 2 job mới) vẫn dựng được
    /// qua DI container thật của Program.cs — build thành công không đảm bảo điều này vì AddScoped
    /// resolve constructor bằng reflection lúc chạy, không kiểm ở compile-time.
    /// </summary>
    public class NotificationDiWiringTests : IntegrationTestBase
    {
        public NotificationDiWiringTests(CustomWebApplicationFactory factory) : base(factory) { }

        [Fact]
        public void AllNotificationConsumingServices_ResolveFromContainer()
        {
            using var scope = Factory.Services.CreateScope();
            var sp = scope.ServiceProvider;

            sp.GetRequiredService<INotificationService>().Should().NotBeNull();
            sp.GetRequiredService<IStockTransferService>().Should().NotBeNull();
            sp.GetRequiredService<ISalesChangeRequestService>().Should().NotBeNull();
            sp.GetRequiredService<IMarketingPostService>().Should().NotBeNull();
            sp.GetRequiredService<IAdminUserService>().Should().NotBeNull();
            sp.GetRequiredService<IOrderService>().Should().NotBeNull();
            sp.GetRequiredService<IWebhookLogService>().Should().NotBeNull();
        }

        [Fact]
        public void AllScheduledJobs_ResolveFromContainer_IncludingNewOnes()
        {
            using var scope = Factory.Services.CreateScope();

            var jobs = scope.ServiceProvider.GetServices<IScheduledJob>().ToList();

            jobs.Should().NotBeEmpty();
            jobs.Select(j => j.JobName).Should().Contain(new[] { "QuotationExpiry", "UpcomingDeliveryReminder" });
            jobs.Should().OnlyContain(j => j != null);
        }
    }
}
