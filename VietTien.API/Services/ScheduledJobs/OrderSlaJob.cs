using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Logic gốc chuyển nguyên trạng từ OrderSlaBackgroundService (không đổi ngưỡng/hành vi):
    // đơn COD PendingConfirmation quá 25' cảnh báo Sale, quá 30' cảnh báo Sales Manager,
    // quá 35' tự động hủy giữ tồn (WF-06, CR-03).
    public class OrderSlaJob : IScheduledJob
    {
        public string JobName => "OrderSla";
        public TimeSpan Interval => TimeSpan.FromMinutes(1);

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IInventoryReservationService _inventoryReservationService;
        private readonly ILogger<OrderSlaJob> _logger;

        public OrderSlaJob(ApplicationDbContext context, INotificationService notificationService, IInventoryReservationService inventoryReservationService, ILogger<OrderSlaJob> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _inventoryReservationService = inventoryReservationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            var pendingOrders = await _context.Orders
                .Include(o => o.CustomerProfile)
                .Include(o => o.OrderItems)
                .Where(o => o.OrderStatus == OrderStatus.PendingConfirmation)
                .ToListAsync(ct);

            int processedCount = 0;

            foreach (var order in pendingOrders)
            {
                try
                {
                    var minutesPassed = (now - order.CreatedAt).TotalMinutes;

                    if (minutesPassed >= 35)
                    {
                        // Gộp release tồn + cập nhật OrderStatus vào 1 transaction duy nhất:
                        // ReleaseReservedAsync giờ tham gia transaction này (không tự commit riêng nữa)
                        // -> nếu SaveChanges thất bại, rollback undo cả 2, tránh trường hợp tồn đã thả
                        // nhưng đơn vẫn hiển thị "đang giữ chỗ".
                        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                        try
                        {
                            // Đơn PendingConfirmation luôn còn giữ mềm (Reserved) từ lúc checkout — trả lại tồn.
                            await _inventoryReservationService.ReleaseReservedAsync(
                                order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)), ct);

                            order.OrderStatus = OrderStatus.Cancelled;
                            order.FulfillmentStatus = FulfillmentStatus.Unallocated;

                            await _context.SaveChangesAsync(ct);
                            await transaction.CommitAsync(ct);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(ct);
                            throw;
                        }

                        _logger.LogInformation("Order {OrderCode} has been cancelled due to SLA timeout.", order.OrderCode);
                    }
                    else if (minutesPassed >= 30)
                    {
                        var sent30 = await _context.Notifications.AnyAsync(n => n.ReferenceId == order.Id && n.Type == NotificationType.SYS_04_CodUnconfirmed30m, ct);
                        if (!sent30)
                        {
                            await _notificationService.CreateRoleNotificationAsync(
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
                        var sent25 = await _context.Notifications.AnyAsync(n => n.ReferenceId == order.Id && n.Type == NotificationType.SYS_03_CodUnconfirmed25m, ct);
                        if (!sent25 && order.CustomerProfile.AssignedSalesStaffId.HasValue)
                        {
                            await _notificationService.CreateNotificationAsync(
                                NotificationType.SYS_03_CodUnconfirmed25m,
                                order.CustomerProfile.AssignedSalesStaffId.Value,
                                "Đơn hàng COD sắp quá hạn",
                                $"Đơn {order.OrderCode} sắp quá 35 phút giữ tồn.",
                                order.Id,
                                "Order"
                            );
                        }
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng đơn: 1 đơn lỗi không được làm dừng cả batch,
                    // các đơn còn lại vẫn phải được xử lý trong cùng lượt chạy.
                    _logger.LogError(ex, "Lỗi xử lý SLA cho đơn {OrderCode} ({OrderId})", order.OrderCode, order.Id);
                }
            }

            if (pendingOrders.Count > 0)
            {
                await _context.SaveChangesAsync(ct);
            }

            return processedCount;
        }
    }
}
