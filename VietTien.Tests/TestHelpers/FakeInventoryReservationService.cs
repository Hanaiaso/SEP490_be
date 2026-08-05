using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Services.Interfaces;

namespace VietTien.Tests.TestHelpers
{
    /// <summary>
    /// Bản giả lập IInventoryReservationService cho test EF InMemory — InMemory không hỗ trợ
    /// ExecuteSqlInterpolatedAsync (raw SQL) nên không thể dùng InventoryReservationService thật.
    /// Bản này tái hiện đúng logic nghiệp vụ (kiểm tra khả dụng trước khi trừ/cộng, ném Exception
    /// nếu không đủ) bằng thao tác entity thuần EF, đủ để unit test xác nhận hành vi service gọi nó.
    /// Không đại diện cho tính atomic dưới concurrency thật (đó là trách nhiệm của bản SQL Server).
    /// </summary>
    public class FakeInventoryReservationService : IInventoryReservationService
    {
        private readonly ApplicationDbContext _context;

        public FakeInventoryReservationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task ReserveAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => IncreaseAsync(items, inv => inv.ReservedQuantity, (inv, v) => inv.ReservedQuantity = v);

        public Task AllocateAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => IncreaseAsync(items, inv => inv.AllocatedQuantity, (inv, v) => inv.AllocatedQuantity = v);

        public Task ReleaseReservedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => DecreaseAsync(items, inv => inv.ReservedQuantity, (inv, v) => inv.ReservedQuantity = v);

        public Task ReleaseAllocatedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => DecreaseAsync(items, inv => inv.AllocatedQuantity, (inv, v) => inv.AllocatedQuantity = v);

        public async Task DeductOnHandAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
        {
            foreach (var (productId, quantity) in Group(items))
            {
                var rows = await _context.Inventories.Where(i => i.ProductId == productId).ToListAsync(ct);
                var totalAvailable = rows.Sum(r => r.AvailableQuantity);
                if (totalAvailable < quantity)
                    throw new Exception($"Sản phẩm {productId} không đủ tồn kho khả dụng (còn {totalAvailable}, cần {quantity}).");

                var remaining = quantity;
                foreach (var row in rows.OrderByDescending(r => r.AvailableQuantity))
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, Math.Max(0, row.AvailableQuantity));
                    if (take <= 0) continue;
                    row.OnHandQuantity -= take;
                    remaining -= take;
                }
            }

            await _context.SaveChangesAsync(ct);
        }

        private static List<(Guid ProductId, int Quantity)> Group(IEnumerable<(Guid ProductId, int Quantity)> items)
            => items.GroupBy(i => i.ProductId).Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity))).ToList();

        private async Task IncreaseAsync(
            IEnumerable<(Guid ProductId, int Quantity)> items,
            Func<API.Models.Inventory, int> getHeld,
            Action<API.Models.Inventory, int> setHeld)
        {
            foreach (var (productId, quantity) in Group(items))
            {
                var rows = await _context.Inventories.Where(i => i.ProductId == productId).ToListAsync();
                var totalAvailable = rows.Sum(r => r.AvailableQuantity);
                if (totalAvailable < quantity)
                    throw new Exception($"Sản phẩm {productId} không đủ tồn kho khả dụng (còn {totalAvailable}, cần {quantity}).");

                var remaining = quantity;
                foreach (var row in rows.OrderByDescending(r => r.AvailableQuantity))
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, Math.Max(0, row.AvailableQuantity));
                    if (take <= 0) continue;
                    setHeld(row, getHeld(row) + take);
                    remaining -= take;
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task DecreaseAsync(
            IEnumerable<(Guid ProductId, int Quantity)> items,
            Func<API.Models.Inventory, int> getHeld,
            Action<API.Models.Inventory, int> setHeld)
        {
            foreach (var (productId, quantity) in Group(items))
            {
                var rows = await _context.Inventories.Where(i => i.ProductId == productId).ToListAsync();
                var remaining = quantity;
                foreach (var row in rows.OrderByDescending(getHeld))
                {
                    if (remaining <= 0) break;
                    var held = getHeld(row);
                    var take = Math.Min(remaining, Math.Max(0, held));
                    if (take <= 0) continue;
                    setHeld(row, held - take);
                    remaining -= take;
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
