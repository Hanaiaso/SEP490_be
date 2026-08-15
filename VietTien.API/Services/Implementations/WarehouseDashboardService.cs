using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class WarehouseDashboardService : IWarehouseDashboardService
    {
        private const int RecentListLimit = 5;
        private const int SlowMovingDaysThreshold = 14; // "trên 2 tuần"

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventoryService;

        public WarehouseDashboardService(ApplicationDbContext context, IInventoryService inventoryService)
        {
            _context = context;
            _inventoryService = inventoryService;
        }

        public async Task<WarehouseDashboardDto> GetDashboardAsync()
        {
            var now = DateTime.UtcNow;
            // "Hôm nay" phải tính theo giờ Việt Nam (UTC+7), không phải theo ngày UTC — nếu không, dữ
            // liệu phát sinh từ 00:00–06:59 giờ VN (vẫn là ngày UTC hôm trước) sẽ bị tính nhầm sang hôm qua.
            var localToday = now.AddHours(7).Date;
            var todayStart = localToday.AddHours(-7);
            var tomorrowStart = todayStart.AddDays(1);

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new WarehouseSummaryDto { Id = w.Id, Name = w.Name, Code = w.Code })
                .ToListAsync();

            // Dùng đúng 1 định nghĩa "tồn thấp" duy nhất (InventoryService.GetLowStockAlertsAsync) —
            // trước đây Dashboard tự tính lại bằng logic khác (chỉ xét Product, so sánh "<" thay vì
            // "<=", bỏ sót hoàn toàn Material) nên số liệu lệch với trang Cảnh báo tồn thấp và Báo cáo.
            var lowStockAlertsCanonical = await _inventoryService.GetLowStockAlertsAsync();

            var slowMovingItems = await _inventoryService.GetSlowMovingItemsAsync(null, SlowMovingDaysThreshold);

            // Các công thức KPI dưới đây PHẢI khớp chính xác điều kiện lọc mà trang bấm-xem-chi-tiết
            // tương ứng đang dùng (WarehouseService.GetOrdersForWarehouseAsync/GetPickTasksAsync) —
            // trước đây bị sửa lệch nhau (vd ConsolidationArea đổi sang chỉ đếm "Consolidated" trong
            // khi tab "Consolidation" vẫn lọc "Ready|Consolidating") khiến số trên KPI card và số
            // trong modal chi tiết không khớp.
            var outbound = new WarehouseOutboundKpiDto
            {
                PendingOrders = await _context.Orders.CountAsync(o =>
                    o.OrderStatus == OrderStatus.Confirmed && o.FulfillmentStatus <= FulfillmentStatus.Allocated && !o.IsExternalOrder),
                PickingInProgress = await _context.PickTasks.CountAsync(p => p.Status == PickTaskStatus.Picking || p.Status == PickTaskStatus.Exception),
                ConsolidationArea = await _context.Orders.CountAsync(o =>
                    o.FulfillmentStatus == FulfillmentStatus.Ready || o.FulfillmentStatus == FulfillmentStatus.Consolidating),
                CompletedToday = await _context.PickTasks.CountAsync(p =>
                    p.Status == PickTaskStatus.Completed && p.CompletedAt != null && p.CompletedAt >= todayStart && p.CompletedAt < tomorrowStart),
            };

            var inbound = new WarehouseInboundKpiDto
            {
                PendingPurchaseOrders = await _context.PurchaseOrders.CountAsync(po => po.Status == PurchaseOrderStatus.SentToWarehouse),
                ReceiptsInProgress = await _context.GoodsReceipts.CountAsync(gr => gr.Status == GoodsReceiptStatus.Draft),
                QualityCheckPending = await _context.QuarantineLogs.CountAsync(q => q.Status == QuarantineStatus.Waiting && q.GoodsReceiptItemId != null),
                ReturnQuarantinePending = await _context.QuarantineLogs.CountAsync(q => q.Status == QuarantineStatus.Waiting && q.OrderId != null),
            };

            var inventoryOps = new WarehouseInventoryKpiDto
            {
                LowStockCount = lowStockAlertsCanonical.Count,
                SlowMovingCount = slowMovingItems.Count,
                TransfersInTransit = await _context.StockTransfers.CountAsync(t => t.Status == StockTransferStatus.Dispatched),
                ActiveWarehouses = warehouses.Count,
            };

            var recentPickTasks = await _context.PickTasks
                .AsNoTracking()
                .Include(p => p.Order).ThenInclude(o => o.CustomerProfile).ThenInclude(cp => cp.User)
                .OrderByDescending(p => p.CreatedAt)
                .Take(RecentListLimit)
                .Select(p => new RecentPickTaskDto
                {
                    Id = p.Id,
                    OrderCode = p.Order.OrderCode,
                    CustomerName = p.Order.CustomerProfile.CompanyName ?? p.Order.CustomerProfile.User.FullName,
                    Status = PickTaskStatusLabel(p.Status),
                })
                .ToListAsync();

            var pendingPurchaseOrders = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(po => po.Supplier)
                .Include(po => po.Items)
                .Where(po => po.Status == PurchaseOrderStatus.SentToWarehouse || po.Status == PurchaseOrderStatus.PartiallyReceived)
                .OrderByDescending(po => po.CreatedAt)
                .Take(RecentListLimit)
                .Select(po => new PendingPurchaseOrderDto
                {
                    Id = po.Id,
                    Code = po.Code,
                    SupplierName = po.Supplier.Name,
                    Status = PurchaseOrderStatusLabel(po.Status),
                    // Trước đây hardcode 50% cho mọi PO "nhận một phần", bất kể thực nhận bao nhiêu.
                    ProgressPercent = po.Items.Sum(i => i.ExpectedQuantity) > 0
                        ? po.Items.Sum(i => i.ReceivedQuantity) * 100 / po.Items.Sum(i => i.ExpectedQuantity)
                        : 0,
                })
                .ToListAsync();

            var lowStockAlerts = lowStockAlertsCanonical
                .Take(RecentListLimit)
                .Select(a => new LowStockItemDto
                {
                    Name = a.ItemName,
                    OnHand = (int)a.AvailableQuantity,
                    Threshold = (int)a.Threshold,
                    Unit = a.Unit ?? string.Empty,
                })
                .ToList();

            var inTransitTransfers = await _context.StockTransfers
                .AsNoTracking()
                .Include(t => t.SourceWarehouse)
                .Include(t => t.DestinationWarehouse)
                .Where(t => t.Status == StockTransferStatus.Dispatched || t.Status == StockTransferStatus.TransportArranged)
                .OrderByDescending(t => t.CreatedAt)
                .Take(RecentListLimit)
                .Select(t => new InTransitTransferDto
                {
                    Id = t.Id,
                    Code = t.Code,
                    SourceWarehouse = t.SourceWarehouse.Name,
                    DestinationWarehouse = t.DestinationWarehouse.Name,
                    Status = t.Status == StockTransferStatus.Dispatched ? "Đang vận chuyển" : "Đã xếp xe",
                })
                .ToListAsync();

            var recentMaterialIssues = await _context.GoodsIssues
                .AsNoTracking()
                .Where(gi => gi.Type == GoodsIssueType.ProductionMaterial)
                .OrderByDescending(gi => gi.CreatedAt)
                .Take(RecentListLimit)
                .Select(gi => new RecentMaterialIssueDto
                {
                    Id = gi.Id,
                    Code = gi.Code,
                    Recipient = gi.Department ?? gi.ExternalRecipientName ?? "N/A",
                    Status = GoodsIssueStatusLabel(gi.Status),
                })
                .ToListAsync();

            return new WarehouseDashboardDto
            {
                GeneratedAt = now,
                Warehouses = warehouses,
                Outbound = outbound,
                Inbound = inbound,
                InventoryOps = inventoryOps,
                RecentPickTasks = recentPickTasks,
                PendingPurchaseOrders = pendingPurchaseOrders,
                LowStockAlerts = lowStockAlerts,
                InTransitTransfers = inTransitTransfers,
                RecentMaterialIssues = recentMaterialIssues,
            };
        }

        private static string PickTaskStatusLabel(PickTaskStatus status) => status switch
        {
            PickTaskStatus.Pending => "Chờ xử lý",
            PickTaskStatus.Picking => "Đang picking",
            PickTaskStatus.Completed => "Hoàn tất",
            PickTaskStatus.Exception => "Ngoại lệ",
            _ => status.ToString(),
        };

        private static string PurchaseOrderStatusLabel(PurchaseOrderStatus status) => status switch
        {
            PurchaseOrderStatus.SentToWarehouse => "Đã phát hành",
            PurchaseOrderStatus.PartiallyReceived => "Nhập 1 phần",
            PurchaseOrderStatus.FullyReceived => "Hoàn tất",
            _ => status.ToString(),
        };

        private static string GoodsIssueStatusLabel(GoodsIssueStatus status) => status switch
        {
            GoodsIssueStatus.Posted => "Đã đăng sổ",
            GoodsIssueStatus.ProofUploaded => "Chờ đăng sổ",
            _ => "Chờ chứng từ",
        };
    }
}
