using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Cảnh báo tồn thấp cho (a) hàng thành phẩm qua Inventory.ReorderThreshold và
    // (b) nguyên vật liệu qua Material.IsBelowSafetyThreshold() (field có sẵn, trước đây chưa job nào dùng).
    public class LowStockAlertJob : IScheduledJob
    {
        private static readonly TimeSpan InventoryAlertCooldown = TimeSpan.FromHours(24);
        private static readonly TimeSpan MaterialAlertCooldown = TimeSpan.FromDays(2); // theo đúng mô tả field LastAlertSentDate

        public string JobName => "LowStockAlert";
        public TimeSpan Interval => TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<LowStockAlertJob> _logger;

        public LowStockAlertJob(ApplicationDbContext context, INotificationService notificationService, ILogger<LowStockAlertJob> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var alertsSent = 0;
            var now = DateTime.UtcNow;

            // --- (a) Hàng thành phẩm (Inventory.ReorderThreshold) ---
            var lowStockInventories = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.ReorderThreshold != null)
                .ToListAsync(ct);

            foreach (var inv in lowStockInventories)
            {
                try
                {
                    if (inv.AvailableQuantity > inv.ReorderThreshold!.Value) continue;

                    var alreadySentRecently = await _context.Notifications.AnyAsync(n =>
                        n.Type == NotificationType.SYS_20_LowStockAlert &&
                        n.ReferenceId == inv.Id &&
                        n.CreatedAt >= now - InventoryAlertCooldown, ct);

                    if (alreadySentRecently) continue;

                    var productName = inv.Product?.Name ?? "(Sản phẩm không xác định)";
                    await SendLowStockAlertAsync(
                        $"Tồn kho thấp: {productName}",
                        $"Sản phẩm {productName} còn khả dụng {inv.AvailableQuantity}, dưới ngưỡng {inv.ReorderThreshold}.",
                        inv.Id, "Inventory");

                    alertsSent++;
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng inventory: 1 dòng lỗi không được chặn cảnh báo cho các dòng khác.
                    _logger.LogError(ex, "Lỗi gửi cảnh báo tồn kho thấp cho Inventory {InventoryId}", inv.Id);
                }
            }

            // --- (b) Nguyên vật liệu (Material.SafetyThreshold) ---
            var materials = await _context.Materials.ToListAsync(ct);

            foreach (var material in materials)
            {
                try
                {
                    if (!material.IsBelowSafetyThreshold()) continue;

                    if (material.LastAlertSentDate.HasValue && now - material.LastAlertSentDate.Value < MaterialAlertCooldown)
                        continue;

                    await SendLowStockAlertAsync(
                        $"Tồn nguyên vật liệu thấp: {material.Name}",
                        $"Vật liệu {material.Name} còn {material.CurrentStock} {material.Unit}, dưới ngưỡng an toàn {material.SafetyThreshold}.",
                        material.Id, "Material");

                    material.LastAlertSentDate = now;
                    // Lưu ngay cho vật liệu này: nếu vật liệu tiếp theo trong batch lỗi, cờ cooldown của
                    // vật liệu này vẫn phải được lưu để tránh gửi cảnh báo trùng ở lượt chạy kế tiếp.
                    await _context.SaveChangesAsync(ct);
                    alertsSent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi cảnh báo tồn nguyên vật liệu thấp cho Material {MaterialId}", material.Id);
                }
            }

            return alertsSent;
        }

        private async Task SendLowStockAlertAsync(string title, string body, Guid referenceId, string referenceType)
        {
            await _notificationService.CreateRoleNotificationAsync(
                NotificationType.SYS_20_LowStockAlert, SystemRole.WarehouseStaff, title, body, referenceId, referenceType);

            await _notificationService.CreateRoleNotificationAsync(
                NotificationType.SYS_20_LowStockAlert, SystemRole.SalesManager, title, body, referenceId, referenceType);
        }
    }
}
