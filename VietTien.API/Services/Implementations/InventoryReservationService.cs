using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietTien.API.Data;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class InventoryReservationService : IInventoryReservationService
    {
        private enum HoldColumn { Reserved, Allocated }

        private readonly ApplicationDbContext _context;

        public InventoryReservationService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Nếu caller đã tự mở sẵn transaction trên cùng _context (vd OrderService.PlaceOrderAsync gộp
        // toàn bộ luồng đặt hàng: giữ tồn + tạo Order + ghi CreditTransaction vào 1 transaction) ->
        // tham gia transaction đó, KHÔNG tự commit/rollback riêng (để nguyên tử thật sự với phần ghi
        // của caller). Nếu không có transaction ambient -> tự mở/commit/rollback như trước, giữ đúng
        // hành vi hiện có cho các luồng khác chưa được gộp.
        private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
        {
            if (_context.Database.CurrentTransaction != null)
                return null;
            return await _context.Database.BeginTransactionAsync(ct);
        }

        public Task ReserveAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => IncreaseAsync(items, HoldColumn.Reserved, ct);

        public Task AllocateAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => IncreaseAsync(items, HoldColumn.Allocated, ct);

        public Task ReleaseReservedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => DecreaseAsync(items, HoldColumn.Reserved, ct);

        public Task ReleaseAllocatedAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
            => DecreaseAsync(items, HoldColumn.Allocated, ct);

        // ──────────────────────────────────────────────────────────
        // TRỪ THẲNG OnHand (bán trực tiếp tại quầy) — atomic guarded UPDATE,
        // cùng cơ chế chống oversell với IncreaseAsync nhưng trừ OnHandQuantity thay vì
        // tăng Reserved/Allocated (hàng rời kho ngay lập tức, không qua vòng đời giữ tồn).
        // ──────────────────────────────────────────────────────────
        public async Task DeductOnHandAsync(IEnumerable<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
        {
            var grouped = GroupByProduct(items);
            if (grouped.Count == 0) return;

            await using var tx = await BeginTransactionIfNeededAsync(ct);
            try
            {
                foreach (var (productId, quantity) in grouped)
                {
                    var rows = await _context.Inventories.AsNoTracking()
                        .Where(inv => inv.ProductId == productId)
                        .Select(inv => new
                        {
                            inv.Id,
                            Available = inv.OnHandQuantity - inv.ReservedQuantity - inv.AllocatedQuantity - inv.DamagedQuantity - inv.QuarantineQuantity
                        })
                        .OrderByDescending(x => x.Available)
                        .ToListAsync(ct);

                    var totalAvailable = rows.Sum(r => Math.Max(0, r.Available));
                    if (totalAvailable < quantity)
                        throw new Exception($"{await ProductLabelAsync(productId, ct)} không đủ tồn kho khả dụng (còn {totalAvailable}, cần {quantity}).");

                    var remaining = quantity;
                    foreach (var row in rows)
                    {
                        if (remaining <= 0) break;
                        var take = Math.Min(remaining, Math.Max(0, row.Available));
                        if (take <= 0) continue;

                        var affected = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                            UPDATE Inventories SET OnHandQuantity = OnHandQuantity - {take}
                            WHERE Id = {row.Id}
                              AND (OnHandQuantity - ReservedQuantity - AllocatedQuantity - DamagedQuantity - QuarantineQuantity) >= {take}", ct);
                        if (affected != 1)
                            throw new Exception($"{await ProductLabelAsync(productId, ct)} vừa hết tồn kho khả dụng do có giao dịch khác xử lý trước, vui lòng thử lại.");

                        remaining -= take;
                    }

                    if (remaining > 0)
                        throw new Exception($"{await ProductLabelAsync(productId, ct)} không đủ tồn kho khả dụng (còn {quantity - remaining}, cần {quantity}).");
                }

                if (tx != null) await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────
        // TĂNG (Reserve / Allocate) — atomic guarded UPDATE, tự chống oversell
        // dưới điều kiện đồng thời mà không cần thêm concurrency token/migration.
        // ──────────────────────────────────────────────────────────
        private async Task IncreaseAsync(IEnumerable<(Guid ProductId, int Quantity)> items, HoldColumn column, CancellationToken ct)
        {
            var grouped = GroupByProduct(items);
            if (grouped.Count == 0) return;

            await using var tx = await BeginTransactionIfNeededAsync(ct);
            try
            {
                foreach (var (productId, quantity) in grouped)
                {
                    var rows = await _context.Inventories.AsNoTracking()
                        .Where(inv => inv.ProductId == productId)
                        .Select(inv => new
                        {
                            inv.Id,
                            Available = inv.OnHandQuantity - inv.ReservedQuantity - inv.AllocatedQuantity - inv.DamagedQuantity - inv.QuarantineQuantity
                        })
                        .OrderByDescending(x => x.Available)
                        .ToListAsync(ct);

                    var totalAvailable = rows.Sum(r => Math.Max(0, r.Available));
                    if (totalAvailable < quantity)
                        throw new Exception($"{await ProductLabelAsync(productId, ct)} không đủ tồn kho khả dụng (còn {totalAvailable}, cần {quantity}).");

                    var remaining = quantity;
                    foreach (var row in rows)
                    {
                        if (remaining <= 0) break;
                        var take = Math.Min(remaining, Math.Max(0, row.Available));
                        if (take <= 0) continue;

                        var affected = await ExecuteGuardedIncrementAsync(row.Id, take, column, ct);
                        if (affected != 1)
                            throw new Exception($"{await ProductLabelAsync(productId, ct)} vừa hết tồn kho khả dụng do có giao dịch khác xử lý trước, vui lòng thử lại.");

                        remaining -= take;
                    }

                    if (remaining > 0)
                        throw new Exception($"{await ProductLabelAsync(productId, ct)} không đủ tồn kho khả dụng (còn {quantity - remaining}, cần {quantity}).");
                }

                if (tx != null) await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(ct);
                throw;
            }
        }

        private Task<int> ExecuteGuardedIncrementAsync(Guid inventoryId, int take, HoldColumn column, CancellationToken ct)
        {
            return column == HoldColumn.Reserved
                ? _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE Inventories SET ReservedQuantity = ReservedQuantity + {take}
                    WHERE Id = {inventoryId}
                      AND (OnHandQuantity - ReservedQuantity - AllocatedQuantity - DamagedQuantity - QuarantineQuantity) >= {take}", ct)
                : _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE Inventories SET AllocatedQuantity = AllocatedQuantity + {take}
                    WHERE Id = {inventoryId}
                      AND (OnHandQuantity - ReservedQuantity - AllocatedQuantity - DamagedQuantity - QuarantineQuantity) >= {take}", ct);
        }

        // ──────────────────────────────────────────────────────────
        // GIẢM (Release) — rút dần từ các dòng đang giữ nhiều nhất, chặn ở 0.
        // Tổng theo sản phẩm luôn bảo toàn (mỗi lần giữ đều có đúng 1 lần trả tương ứng),
        // nên không cần lưu vết dòng Inventory cụ thể đã giữ ban đầu.
        // ──────────────────────────────────────────────────────────
        private async Task DecreaseAsync(IEnumerable<(Guid ProductId, int Quantity)> items, HoldColumn column, CancellationToken ct)
        {
            var grouped = GroupByProduct(items);
            if (grouped.Count == 0) return;

            await using var tx = await BeginTransactionIfNeededAsync(ct);
            try
            {
                foreach (var (productId, quantity) in grouped)
                {
                    var rows = await _context.Inventories.AsNoTracking()
                        .Where(inv => inv.ProductId == productId)
                        .Select(inv => new { inv.Id, Held = column == HoldColumn.Reserved ? inv.ReservedQuantity : inv.AllocatedQuantity })
                        .OrderByDescending(x => x.Held)
                        .ToListAsync(ct);

                    var remaining = quantity;
                    foreach (var row in rows)
                    {
                        if (remaining <= 0) break;
                        var take = Math.Min(remaining, Math.Max(0, row.Held));
                        if (take <= 0) continue;

                        await ExecuteGuardedDecrementAsync(row.Id, take, column, ct);
                        remaining -= take;
                    }
                    // remaining > 0 sau vòng lặp nghĩa là đã giải phóng hết mức đang giữ (có thể do
                    // tồn kho bị điều chỉnh thủ công ở giữa) — không coi là lỗi, không âm hoá số liệu.
                }

                if (tx != null) await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(ct);
                throw;
            }
        }

        private Task ExecuteGuardedDecrementAsync(Guid inventoryId, int take, HoldColumn column, CancellationToken ct)
        {
            return column == HoldColumn.Reserved
                ? _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE Inventories SET ReservedQuantity = CASE WHEN ReservedQuantity - {take} < 0 THEN 0 ELSE ReservedQuantity - {take} END
                    WHERE Id = {inventoryId}", ct)
                : _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE Inventories SET AllocatedQuantity = CASE WHEN AllocatedQuantity - {take} < 0 THEN 0 ELSE AllocatedQuantity - {take} END
                    WHERE Id = {inventoryId}", ct);
        }

        private async Task<string> ProductLabelAsync(Guid productId, CancellationToken ct)
        {
            var name = await _context.Products.Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            return $"Sản phẩm '{name ?? productId.ToString()}'";
        }

        private static List<(Guid ProductId, int Quantity)> GroupByProduct(IEnumerable<(Guid ProductId, int Quantity)> items)
            => items
                .GroupBy(i => i.ProductId)
                .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
                .Where(x => x.Quantity > 0)
                .ToList();
    }
}
