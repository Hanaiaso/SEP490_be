using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Nhắc trước 1 ngày cho các đơn đã xếp lịch giao hàng (DeliveryStatus=Scheduled,
    // ScheduledDeliveryDate = ngày mai theo giờ VN) để nhân viên kho/Sale chủ động chuẩn bị.
    public class UpcomingDeliveryReminderJob : IScheduledJob
    {
        private static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(20);

        public string JobName => "UpcomingDeliveryReminder";
        public TimeSpan Interval => TimeSpan.FromHours(6);

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<UpcomingDeliveryReminderJob> _logger;

        public UpcomingDeliveryReminderJob(ApplicationDbContext context, INotificationService notificationService, ILogger<UpcomingDeliveryReminderJob> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var localTomorrow = now.AddHours(7).Date.AddDays(1);

            var upcomingOrders = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Where(o => o.DeliveryStatus == DeliveryStatus.Scheduled
                            && o.ScheduledDeliveryDate != null
                            && o.ScheduledDeliveryDate.Value.Date == localTomorrow)
                .ToListAsync(ct);

            var remindedCount = 0;

            foreach (var order in upcomingOrders)
            {
                try
                {
                    var alreadyRemindedRecently = await _context.Notifications.AnyAsync(n =>
                        n.Type == NotificationType.SYS_29_UpcomingDeliveryReminder &&
                        n.ReferenceId == order.Id &&
                        n.CreatedAt >= now - ReminderCooldown, ct);

                    if (alreadyRemindedRecently) continue;

                    var shiftText = string.IsNullOrEmpty(order.DeliveryShift) ? "" : $" ca {order.DeliveryShift}";
                    var message = $"Đơn hàng {order.OrderCode} có lịch giao ngày mai ({order.ScheduledDeliveryDate:dd/MM/yyyy}{shiftText}).";

                    if (order.WarehouseStaffId.HasValue)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_29_UpcomingDeliveryReminder,
                            order.WarehouseStaffId.Value,
                            "Nhắc lịch giao hàng ngày mai",
                            message,
                            order.Id,
                            "Order");
                    }

                    if (order.CustomerProfile?.AssignedSalesStaffId != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_29_UpcomingDeliveryReminder,
                            order.CustomerProfile.AssignedSalesStaffId.Value,
                            "Nhắc lịch giao hàng ngày mai",
                            message,
                            order.Id,
                            "Order");
                    }

                    remindedCount++;
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng đơn: 1 đơn lỗi không được chặn nhắc lịch cho các đơn khác.
                    _logger.LogError(ex, "Lỗi gửi nhắc lịch giao hàng cho đơn {OrderId}", order.Id);
                }
            }

            return remindedCount;
        }
    }
}
