using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class CeoDashboardService : ICeoDashboardService
    {
        private const int RecentPoLimit = 10;
        private static readonly PurchaseOrderStatus[] OpenStatuses =
        {
            PurchaseOrderStatus.Draft, PurchaseOrderStatus.Issued, PurchaseOrderStatus.SentToWarehouse,
            PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.DiscrepancyReview
        };

        private readonly ApplicationDbContext _context;
        private readonly IKpiService _kpiService;

        public CeoDashboardService(ApplicationDbContext context, IKpiService kpiService)
        {
            _context = context;
            _kpiService = kpiService;
        }

        public async Task<CeoDashboardDto> GetDashboardAsync(DateTime from, DateTime to)
        {
            var orgKpi = await _kpiService.GetSnapshotAsync(null, from, to);

            return new CeoDashboardDto
            {
                OrgKpi = orgKpi,
                Inventory = await GetInventorySummaryAsync(),
                PurchaseOrders = await GetPurchaseOrderSummaryAsync(),
                Discrepancy = await GetDiscrepancySummaryAsync(from, to)
            };
        }

        private async Task<InventorySummaryDto> GetInventorySummaryAsync()
        {
            var totalSkus = await _context.Inventories
                .Where(i => i.ProductId != null)
                .Select(i => i.ProductId)
                .Distinct()
                .CountAsync();

            // AvailableQuantity là computed property (Math.Max trên C#), không dịch được sang SQL ->
            // lấy tập đã lọc ReorderThreshold trước (thường nhỏ) rồi so sánh ở memory, giống LowStockAlertJob (Phase 2).
            var thresholdedInventories = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.ReorderThreshold != null)
                .ToListAsync();
            var lowStockCount = thresholdedInventories.Count(i => i.AvailableQuantity < i.ReorderThreshold!.Value);

            var estimatedValue = await _context.Inventories
                .Where(i => i.ProductId != null)
                .SumAsync(i => (decimal?)(i.OnHandQuantity * i.Product!.StandardListedPrice)) ?? 0m;

            return new InventorySummaryDto
            {
                TotalSkus = totalSkus,
                LowStockCount = lowStockCount,
                EstimatedInventoryValue = estimatedValue
            };
        }

        private async Task<PurchaseOrderSummaryDto> GetPurchaseOrderSummaryAsync()
        {
            var grouped = await _context.PurchaseOrders
                .AsNoTracking()
                .GroupBy(po => po.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var recentOpen = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(po => po.Supplier)
                .Where(po => OpenStatuses.Contains(po.Status))
                .OrderByDescending(po => po.CreatedAt)
                .Take(RecentPoLimit)
                .Select(po => new PurchaseOrderSummaryItemDto
                {
                    Id = po.Id,
                    Code = po.Code,
                    Status = po.Status.ToString(),
                    SupplierName = po.Supplier.Name,
                    CreatedAt = po.CreatedAt,
                    ExpectedDeliveryDate = po.ExpectedDeliveryDate
                })
                .ToListAsync();

            return new PurchaseOrderSummaryDto
            {
                CountsByStatus = grouped.ToDictionary(g => g.Status.ToString(), g => g.Count),
                RecentOpenPurchaseOrders = recentOpen
            };
        }

        private async Task<DiscrepancySummaryDto> GetDiscrepancySummaryAsync(DateTime from, DateTime to)
        {
            var items = _context.GoodsReceiptItems
                .Where(i => i.GoodsReceipt.ReceivedDate >= from && i.GoodsReceipt.ReceivedDate <= to);

            var receiptCount = await items.Select(i => i.GoodsReceiptId).Distinct().CountAsync();
            var totalShort = await items.SumAsync(i => (int?)i.ShortQuantity) ?? 0;
            var totalExcess = await items.SumAsync(i => (int?)i.ExcessQuantity) ?? 0;
            var totalDamaged = await items.SumAsync(i => (int?)i.DamagedQuantity) ?? 0;
            var totalWrongItem = await items.SumAsync(i => (int?)i.WrongItemQuantity) ?? 0;

            return new DiscrepancySummaryDto
            {
                PeriodFrom = from,
                PeriodTo = to,
                GoodsReceiptCount = receiptCount,
                TotalShortQuantity = totalShort,
                TotalExcessQuantity = totalExcess,
                TotalDamagedQuantity = totalDamaged,
                TotalWrongItemQuantity = totalWrongItem
            };
        }
    }
}
