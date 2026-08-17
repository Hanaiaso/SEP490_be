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
        private readonly IInventoryService _inventoryService;

        public CeoDashboardService(ApplicationDbContext context, IKpiService kpiService, IInventoryService inventoryService)
        {
            _context = context;
            _kpiService = kpiService;
            _inventoryService = inventoryService;
        }

        public async Task<CeoDashboardDto> GetDashboardAsync(DateTime from, DateTime to)
        {
            var orgKpi = await _kpiService.GetSnapshotAsync(null, from, to);
            var pendingCeoQuotationCount = await _context.Quotations
                .CountAsync(q => q.Status == QuotationStatus.PendingCeo);

            return new CeoDashboardDto
            {
                OrgKpi = orgKpi,
                Inventory = await GetInventorySummaryAsync(),
                PurchaseOrders = await GetPurchaseOrderSummaryAsync(),
                Discrepancy = await GetDiscrepancySummaryAsync(from, to),
                PendingCeoQuotationCount = pendingCeoQuotationCount
            };
        }

        private async Task<InventorySummaryDto> GetInventorySummaryAsync()
        {
            var totalSkus = await _context.Inventories
                .Where(i => i.ProductId != null)
                .Select(i => i.ProductId)
                .Distinct()
                .CountAsync();

            // Dùng lại InventoryService (nguồn xác thực duy nhất, gộp cả Product + Material qua mọi kho)
            // thay vì tự tính lại — trước đây đọc thẳng Inventory.ReorderThreshold (field per-row đã chết,
            // không có nơi nào set) nên LowStockCount trên Dashboard CEO luôn = 0 dù thực tế có hàng cảnh báo.
            var lowStockCount = (await _inventoryService.GetLowStockAlertsAsync()).Count;
            var excessStockCount = (await _inventoryService.GetExcessStockAlertsAsync()).Count;

            var estimatedValue = await _context.Inventories
                .Where(i => i.ProductId != null)
                .SumAsync(i => (decimal?)(i.OnHandQuantity * i.Product!.StandardListedPrice)) ?? 0m;

            return new InventorySummaryDto
            {
                TotalSkus = totalSkus,
                LowStockCount = lowStockCount,
                ExcessStockCount = excessStockCount,
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
