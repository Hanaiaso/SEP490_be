using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Cảnh báo tồn thấp + tồn đọng cho (a) hàng thành phẩm qua Product.ReorderThreshold/ExcessThreshold và
    // (b) nguyên vật liệu qua Material.SafetyThreshold/MaxStockThreshold, so với tồn tính từ
    // Inventories.Sum(AvailableQuantity) (không dùng Material.CurrentStock trực tiếp vì field đó không được
    // đồng bộ khi kho điều chỉnh tồn). Ngưỡng và cooldown đều tính GỘP theo sản phẩm/vật liệu (tổng mọi kho),
    // không theo từng Inventory row — 1 mặt hàng không thể vừa tồn thấp vừa tồn đọng cùng lúc nên dùng
    // chung 1 field cooldown (LastAlertSentDate) cho cả 2 loại cảnh báo.
    public class LowStockAlertJob : IScheduledJob
    {
        private static readonly TimeSpan ProductAlertCooldown = TimeSpan.FromHours(24);
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

            // --- (a) Hàng thành phẩm (Product.ReorderThreshold / ExcessThreshold) ---
            var products = await _context.Products
                .Include(p => p.Inventories)
                .Where(p => p.ReorderThreshold != null || p.ExcessThreshold != null)
                .ToListAsync(ct);

            foreach (var product in products)
            {
                try
                {
                    if (product.LastAlertSentDate.HasValue && now - product.LastAlertSentDate.Value < ProductAlertCooldown)
                        continue;

                    var available = product.Inventories.Sum(i => i.AvailableQuantity);

                    if (product.ReorderThreshold != null && available <= product.ReorderThreshold.Value)
                    {
                        await SendLowStockAlertAsync(
                            $"Tồn kho thấp: {product.Name}",
                            $"Sản phẩm {product.Name} còn khả dụng {available}, dưới ngưỡng {product.ReorderThreshold}.",
                            product.Id, "Product");

                        product.LastAlertSentDate = now;
                        await _context.SaveChangesAsync(ct);
                        alertsSent++;
                    }
                    else if (product.ExcessThreshold != null && available > product.ExcessThreshold.Value)
                    {
                        await SendExcessStockAlertAsync(
                            $"Tồn đọng: {product.Name}",
                            $"Sản phẩm {product.Name} đang tồn {available}, vượt ngưỡng {product.ExcessThreshold}.",
                            product.Id, "Product");

                        product.LastAlertSentDate = now;
                        await _context.SaveChangesAsync(ct);
                        alertsSent++;
                    }
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng sản phẩm: 1 dòng lỗi không được chặn cảnh báo cho các sản phẩm khác.
                    _logger.LogError(ex, "Lỗi gửi cảnh báo tồn kho cho Product {ProductId}", product.Id);
                }
            }

            // --- (b) Nguyên vật liệu (Material.SafetyThreshold / MaxStockThreshold) ---
            // Include Inventories: Material.CurrentStock luôn = 0 và không nơi nào khác cập nhật field này
            // khi nhân viên kho điều chỉnh tồn qua Inventory (xem comment ở MaterialService.MapToDto) ->
            // phải tính tồn thực tế từ Inventories.Sum(AvailableQuantity), nếu không job sẽ luôn coi tồn = 0
            // và báo động sai dù kho thực tế đã đủ hàng.
            var materials = await _context.Materials.Include(m => m.Inventories).ToListAsync(ct);

            foreach (var material in materials)
            {
                try
                {
                    if (material.LastAlertSentDate.HasValue && now - material.LastAlertSentDate.Value < MaterialAlertCooldown)
                        continue;

                    var calculatedStock = material.Inventories.Any()
                        ? material.Inventories.Sum(i => i.AvailableQuantity)
                        : material.CurrentStock;

                    if (calculatedStock <= material.SafetyThreshold)
                    {
                        await SendLowStockAlertAsync(
                            $"Tồn nguyên vật liệu thấp: {material.Name}",
                            $"Vật liệu {material.Name} còn {calculatedStock} {material.Unit}, dưới ngưỡng an toàn {material.SafetyThreshold}.",
                            material.Id, "Material");

                        material.LastAlertSentDate = now;
                        // Lưu ngay cho vật liệu này: nếu vật liệu tiếp theo trong batch lỗi, cờ cooldown của
                        // vật liệu này vẫn phải được lưu để tránh gửi cảnh báo trùng ở lượt chạy kế tiếp.
                        await _context.SaveChangesAsync(ct);
                        alertsSent++;
                    }
                    else if (material.MaxStockThreshold != null && calculatedStock > material.MaxStockThreshold.Value)
                    {
                        await SendExcessStockAlertAsync(
                            $"Tồn đọng nguyên vật liệu: {material.Name}",
                            $"Vật liệu {material.Name} đang tồn {calculatedStock} {material.Unit}, vượt ngưỡng {material.MaxStockThreshold}.",
                            material.Id, "Material");

                        material.LastAlertSentDate = now;
                        await _context.SaveChangesAsync(ct);
                        alertsSent++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi cảnh báo tồn nguyên vật liệu cho Material {MaterialId}", material.Id);
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

        private async Task SendExcessStockAlertAsync(string title, string body, Guid referenceId, string referenceType)
        {
            await _notificationService.CreateRoleNotificationAsync(
                NotificationType.SYS_50_ExcessStockAlert, SystemRole.WarehouseStaff, title, body, referenceId, referenceType);

            await _notificationService.CreateRoleNotificationAsync(
                NotificationType.SYS_50_ExcessStockAlert, SystemRole.SalesManager, title, body, referenceId, referenceType);
        }
    }
}
