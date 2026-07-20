using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.BackgroundServices
{
    public class OrderSlaBackgroundService : BackgroundService
    {
        private readonly ILogger<OrderSlaBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public OrderSlaBackgroundService(ILogger<OrderSlaBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                        var now = DateTime.UtcNow;

                        var pendingOrders = await context.Orders
                            .Include(o => o.CustomerProfile)
                            .Where(o => o.OrderStatus == OrderStatus.PendingConfirmation)
                            .ToListAsync(stoppingToken);

                        foreach (var order in pendingOrders)
                        {
                            var minutesPassed = (now - order.CreatedAt).TotalMinutes;

                            if (minutesPassed >= 35)
                            {
                                order.OrderStatus = OrderStatus.Cancelled;
                                order.FulfillmentStatus = FulfillmentStatus.Unallocated;
                                _logger.LogInformation($"Order {order.OrderCode} has been cancelled due to SLA timeout.");
                            }
                            else if (minutesPassed >= 30)
                            {
                                var sent30 = await context.Notifications.AnyAsync(n => n.ReferenceId == order.Id && n.Type == NotificationType.SYS_04_CodUnconfirmed30m, stoppingToken);
                                if (!sent30)
                                {
                                    await notificationService.CreateRoleNotificationAsync(
                                        NotificationType.SYS_04_CodUnconfirmed30m,
                                        SystemRole.SalesManager,
                                        "Đơn hàng COD quá hạn 30 phút",
                                        $"Đơn {order.OrderCode} chưa được xác nhận bởi Sale.",
                                        order.Id,
                                        "Order"
                                    );
                                }
                            }
                            else if (minutesPassed >= 25)
                            {
                                var sent25 = await context.Notifications.AnyAsync(n => n.ReferenceId == order.Id && n.Type == NotificationType.SYS_03_CodUnconfirmed25m, stoppingToken);
                                if (!sent25 && order.CustomerProfile.AssignedSalesStaffId.HasValue)
                                {
                                    await notificationService.CreateNotificationAsync(
                                        NotificationType.SYS_03_CodUnconfirmed25m,
                                        order.CustomerProfile.AssignedSalesStaffId.Value,
                                        "Đơn hàng COD sắp quá hạn",
                                        $"Đơn {order.OrderCode} sắp quá 35 phút giữ tồn.",
                                        order.Id,
                                        "Order"
                                    );
                                }
                            }
                        }

                        if (pendingOrders.Any())
                        {
                            await context.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in OrderSlaBackgroundService.");
                }

                // Chạy mỗi 1 phút
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
