using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.ScheduledJobs
{
    // Giữ chỗ tồn kho theo giỏ hàng (thêm vào giỏ là giữ chỗ ngay, xem CartService) chỉ có hiệu lực
    // trong cùng cửa sổ 24h với giữ giá BR-025 (CartPriceLockHelper) — quá hạn mà khách vẫn chưa
    // thanh toán/làm mới giá thì phải nhả chỗ lại cho khách khác, không được giữ hàng vô thời hạn.
    public class CartReservationExpiryJob : IScheduledJob
    {
        public string JobName => "CartReservationExpiry";
        public TimeSpan Interval => TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IInventoryReservationService _inventoryReservationService;
        private readonly ILogger<CartReservationExpiryJob> _logger;

        public CartReservationExpiryJob(ApplicationDbContext context, IInventoryReservationService inventoryReservationService, ILogger<CartReservationExpiryJob> logger)
        {
            _context = context;
            _inventoryReservationService = inventoryReservationService;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct)
        {
            // ReservationReleasedAt == null -> đang giữ chỗ, cần lọc thêm theo hết hạn giữ giá (>24h)
            // ở phía C# vì CartPriceLockHelper không dịch được sang SQL.
            var activeItems = await _context.CartItems
                .Include(ci => ci.Cart)
                .Where(ci => ci.ReservationReleasedAt == null)
                .ToListAsync(ct);

            var now = DateTime.UtcNow;
            var expiredItems = activeItems
                .Where(ci => CartPriceLockHelper.IsItemExpired(ci.PriceLockedAt, ci.Cart.UpdatedAt))
                .ToList();

            var processedCount = 0;

            foreach (var item in expiredItems)
            {
                try
                {
                    await _inventoryReservationService.ReleaseReservedAsync(new[] { (item.ProductId, item.Quantity) }, ct);
                    item.ReservationReleasedAt = now;
                    await _context.SaveChangesAsync(ct);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    // Cô lập lỗi theo từng dòng giỏ: 1 dòng lỗi không được chặn xử lý các dòng khác.
                    _logger.LogError(ex, "Lỗi nhả giữ chỗ tồn kho cho CartItem {CartItemId}", item.Id);
                }
            }

            return processedCount;
        }
    }
}
