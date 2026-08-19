using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Cảnh báo chủ động cho mặt hàng chậm luân chuyển (không có giao dịch xuất kho trong X ngày) —
    // tái dùng đúng query đã có ở InventoryService.GetSlowMovingItemsAsync (không viết lại logic tính),
    // chỉ thêm phần gửi notification + cooldown per-row (mirror LowStockAlertJob).
    public class SlowMovingStockAlertJob : IScheduledJob
    {
        private const int DefaultSlowMovingDaysThreshold = 30;
        private static readonly TimeSpan AlertCooldown = TimeSpan.FromDays(2); // giống Material.LastAlertSentDate

        public string JobName => "SlowMovingStockAlert";
        public TimeSpan Interval => TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventoryService;
        private readonly INotificationService _notificationService;
        private readonly ISystemConfigService _systemConfigService;
        private readonly ILogger<SlowMovingStockAlertJob> _logger;

        public SlowMovingStockAlertJob(
            ApplicationDbContext context,
            IInventoryService inventoryService,
            INotificationService notificationService,
            ISystemConfigService systemConfigService,
            ILogger<SlowMovingStockAlertJob> logger)
        {
            _context = context;
            _inventoryService = inventoryService;
            _notificationService = notificationService;
            _systemConfigService = systemConfigService;
            _logger = logger;
        }

        // CEO cấu hình được qua SLOW_MOVING_DAYS_THRESHOLD (api/ceo/system-configs) — fallback về
        // mặc định 30 ngày nếu chưa cấu hình hoặc giá trị không hợp lệ.
        private async Task<int> GetSlowMovingDaysThresholdAsync()
        {
            var raw = await _systemConfigService.GetEffectiveValueAsync("SLOW_MOVING_DAYS_THRESHOLD");
            return int.TryParse(raw, out var days) && days > 0 ? days : DefaultSlowMovingDaysThreshold;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var alertsSent = 0;
            var now = DateTime.UtcNow;

            var daysThreshold = await GetSlowMovingDaysThresholdAsync();
            var slowMovingItems = await _inventoryService.GetSlowMovingItemsAsync(warehouseId: null, days: daysThreshold);

            foreach (var item in slowMovingItems)
            {
                try
                {
                    var inventory = await _context.Inventories.FirstOrDefaultAsync(i => i.Id == item.Id, ct);
                    if (inventory == null) continue;

                    if (inventory.LastSlowMovingAlertSentAt.HasValue && now - inventory.LastSlowMovingAlertSentAt.Value < AlertCooldown)
                        continue;

                    var title = $"Chậm luân chuyển: {item.ItemName}";
                    var body = item.DaysSinceLastOutbound.HasValue
                        ? $"{item.ItemName} không xuất kho trong {item.DaysSinceLastOutbound} ngày, còn tồn {item.OnHandQuantity} {item.Unit}. Gợi ý: {item.Suggestion}."
                        : $"{item.ItemName} chưa từng xuất kho, đang tồn {item.OnHandQuantity} {item.Unit}. Gợi ý: {item.Suggestion}.";

                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_51_SlowMovingStockAlert, SystemRole.WarehouseStaff, title, body, item.Id, item.ItemType);
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_51_SlowMovingStockAlert, SystemRole.SalesManager, title, body, item.Id, item.ItemType);

                    inventory.LastSlowMovingAlertSentAt = now;
                    // Lưu ngay cho dòng này: nếu dòng tiếp theo lỗi, cờ cooldown của dòng này vẫn được
                    // lưu để tránh gửi cảnh báo trùng ở lượt chạy kế tiếp.
                    await _context.SaveChangesAsync(ct);
                    alertsSent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi cảnh báo chậm luân chuyển cho Inventory {InventoryId}", item.Id);
                }
            }

            return alertsSent;
        }
    }
}
