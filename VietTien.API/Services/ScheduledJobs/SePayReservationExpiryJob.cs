using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // WF-06 / CR-02: đơn thanh toán SePay giữ tồn SEPAY_RESERVATION_MINUTES (mặc định 15 phút,
    // cấu hình qua Admin System Config). Quá hạn mà vẫn Draft + PaymentStatus=Pending thì tự hủy
    // và trả lại phần Inventory.ReservedQuantity đã giữ tại checkout (PlaceOrderAsync).
    public class SePayReservationExpiryJob : IScheduledJob
    {
        private const int DefaultReservationMinutes = 15;

        public string JobName => "SePayReservationExpiry";
        public TimeSpan Interval => TimeSpan.FromMinutes(1);

        private readonly ApplicationDbContext _context;
        private readonly ISystemConfigService _systemConfigService;
        private readonly IInventoryReservationService _inventoryReservationService;
        private readonly ILogger<SePayReservationExpiryJob> _logger;

        public SePayReservationExpiryJob(ApplicationDbContext context, ISystemConfigService systemConfigService, IInventoryReservationService inventoryReservationService, ILogger<SePayReservationExpiryJob> logger)
        {
            _context = context;
            _systemConfigService = systemConfigService;
            _inventoryReservationService = inventoryReservationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            var reservationMinutes = await GetReservationMinutesAsync();
            var cutoff = DateTime.UtcNow.AddMinutes(-reservationMinutes);

            var expiredOrders = await _context.Orders
                .Include(o => o.OrderItems)
                .Where(o => o.OrderStatus == OrderStatus.Draft
                            && o.PaymentMethod != PaymentMethod.COD
                            && o.PaymentStatus == PaymentStatus.Pending
                            && o.CreatedAt < cutoff)
                .ToListAsync(ct);

            int processedCount = 0;

            foreach (var order in expiredOrders)
            {
                try
                {
                    // Gộp release tồn + cập nhật OrderStatus vào 1 transaction duy nhất:
                    // ReleaseReservedAsync giờ tham gia transaction này (không tự commit riêng nữa)
                    // -> nếu SaveChanges thất bại, rollback undo cả 2, tránh trường hợp tồn đã thả
                    // nhưng đơn vẫn hiển thị "đang giữ chỗ".
                    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                    try
                    {
                        await _inventoryReservationService.ReleaseReservedAsync(
                            order.OrderItems.Select(oi => (oi.ProductId, oi.Quantity)), ct);

                        order.OrderStatus = OrderStatus.Cancelled;
                        order.CancelReason = "Hết hạn giữ tồn SePay (tự động).";

                        await _context.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct);
                        throw;
                    }

                    _logger.LogInformation("Order {OrderCode} cancelled: SePay reservation expired after {Minutes}m.", order.OrderCode, reservationMinutes);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi hủy đơn hết hạn giữ tồn SePay cho đơn {OrderCode} ({OrderId})", order.OrderCode, order.Id);
                }
            }

            return processedCount;
        }

        private async Task<int> GetReservationMinutesAsync()
        {
            var raw = await _systemConfigService.GetEffectiveValueAsync("SEPAY_RESERVATION_MINUTES");
            return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : DefaultReservationMinutes;
        }
    }
}
