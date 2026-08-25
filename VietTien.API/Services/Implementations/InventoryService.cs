using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Exceptions;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class InventoryService : IInventoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<WarehouseHub> _warehouseHub;
        private readonly ILogger<InventoryService> _logger;
        private readonly IWarehouseAccessGuard _warehouseAccessGuard;

        public InventoryService(
            ApplicationDbContext context,
            IHubContext<WarehouseHub> warehouseHub,
            ILogger<InventoryService> logger,
            IWarehouseAccessGuard warehouseAccessGuard)
        {
            _context = context;
            _warehouseHub = warehouseHub;
            _logger = logger;
            _warehouseAccessGuard = warehouseAccessGuard;
        }

        public async Task<PaginatedList<InventoryItemDto>> GetInventoryByWarehouseAsync(Guid warehouseId, string? search, int? minQty, int? maxQty, DateTime? fromDate, DateTime? toDate, string? itemType, int pageNumber, int pageSize)
        {
            var query = _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.Material)
                .Include(i => i.LastUpdatedByUser)
                .Include(i => i.WarehouseLocation)
                .Where(i => i.WarehouseLocation.WarehouseId == warehouseId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(i => 
                    (i.Product != null && (i.Product.Name.ToLower().Contains(lowerSearch) || i.Product.Sku.ToLower().Contains(lowerSearch))) ||
                    (i.Material != null && i.Material.Name.ToLower().Contains(lowerSearch)));
            }

            if (minQty.HasValue)
            {
                query = query.Where(i => i.OnHandQuantity >= minQty.Value);
            }

            if (maxQty.HasValue)
            {
                query = query.Where(i => i.OnHandQuantity <= maxQty.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(i => i.LastUpdatedAt >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                // Include the whole day of toDate
                var nextDay = toDate.Value.Date.AddDays(1);
                query = query.Where(i => i.LastUpdatedAt < nextDay);
            }

            if (!string.IsNullOrWhiteSpace(itemType))
            {
                query = itemType == "Material"
                    ? query.Where(i => i.MaterialId != null)
                    : query.Where(i => i.ProductId != null);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(i => i.Product != null ? i.Product.Name : i.Material != null ? i.Material.Name : "")
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new InventoryItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ItemName = i.Product != null ? i.Product.Name : i.Material != null ? i.Material.Name : "N/A",
                    ItemSku = i.Product != null ? i.Product.Sku : "",
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    Unit = i.Product != null ? i.Product.Unit : i.Material != null ? i.Material.Unit : "",
                    OnHandQuantity = i.OnHandQuantity,
                    ReservedQuantity = i.ReservedQuantity,
                    AvailableQuantity = i.AvailableQuantity,
                    LastUpdatedByUserId = i.LastUpdatedByUserId,
                    LastUpdatedByUserName = i.LastUpdatedByUser != null ? i.LastUpdatedByUser.FullName : null,
                    LastUpdatedAt = i.LastUpdatedAt
                })
                .ToListAsync();

            return new PaginatedList<InventoryItemDto>
            {
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Items = items
            };
        }

        public async Task AdjustInventoryAsync(Guid inventoryId, int newQuantity, string? note, Guid staffId)
        {
            if (newQuantity < 0)
                throw new Exception("Số lượng tồn kho điều chỉnh không được âm.");

            var inventory = await _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.Material)
                .Include(i => i.WarehouseLocation)
                .ThenInclude(wl => wl.Warehouse)
                .FirstOrDefaultAsync(i => i.Id == inventoryId);

            if (inventory == null)
            {
                throw new KeyNotFoundException("Inventory record not found.");
            }

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, inventory.WarehouseLocation.WarehouseId,
                "điều chỉnh tồn kho", "Inventory", inventory.Id.ToString());

            var staff = await _context.Users.FindAsync(staffId);

            // L3-INV-04: chặn điều chỉnh khiến tồn khả dụng THỰC âm — trước đây chỉ chặn newQuantity < 0,
            // nên set OnHand về 0 trong khi Reserved/Allocated/Quarantine còn giữ hàng vẫn được chấp nhận,
            // và mức âm thực sự bị AvailableQuantity (Models/Inventory.cs) che mất qua Math.Max(0, ...).
            var rawAvailableAfterAdjust = newQuantity - inventory.ReservedQuantity - inventory.AllocatedQuantity
                - inventory.DamagedQuantity - inventory.QuarantineQuantity;
            if (rawAvailableAfterAdjust < 0)
                throw new Exception(
                    "Điều chỉnh làm tồn khả dụng thực âm (đang giữ Reserved/Allocated/Damaged/Quarantine vượt số lượng mới). " +
                    "Vui lòng giải phóng/điều chỉnh các khoản đang giữ trước.");

            var oldQuantity = inventory.OnHandQuantity;
            inventory.OnHandQuantity = newQuantity;
            inventory.LastUpdatedByUserId = staffId;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            // GH-12/BR-044/BR-022: mọi điều chỉnh tồn phải để lại vết StockTransaction append-only.
            _context.StockTransactions.Add(new StockTransaction
            {
                InventoryId = inventory.Id,
                ProductId = inventory.ProductId,
                MaterialId = inventory.MaterialId,
                WarehouseLocationId = inventory.WarehouseLocationId,
                QuantityChange = newQuantity - oldQuantity,
                TransactionType = TransactionType.StockAdjustment,
                Note = note,
                CreatedByUserId = staffId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            try
            {
                // Send Notification to CEO
                var staffName = staff?.FullName ?? "Nhân viên";
                var warehouseName = inventory.WarehouseLocation.Warehouse?.Name ?? "Kho không xác định";
                var itemName = inventory.Product?.Name ?? inventory.Material?.Name ?? "N/A";

                var message = $"[Cập nhật Tồn Kho] {staffName} vừa cập nhật số lượng của '{itemName}' trong {warehouseName} từ {oldQuantity} thành {newQuantity}.";

                await _warehouseHub.Clients.All.SendAsync("ReceiveNotification", message);
            }
            catch (Exception ex)
            {
                // Tồn kho đã cập nhật và commit thành công ở trên -> lỗi gửi SignalR không được làm
                // request báo lỗi cho client, chỉ log để theo dõi.
                _logger.LogError(ex, "Lỗi gửi thông báo SignalR sau khi điều chỉnh Inventory {InventoryId}", inventoryId);
            }
        }

        public async Task<InventoryItemDto> AddProductToWarehouseAsync(AddInventoryRequest request, Guid staffId)
        {
            // Validate: phải có đúng 1 trong 2
            if (request.ProductId == null && request.MaterialId == null)
                throw new Exception("Phải cung cấp ProductId hoặc MaterialId.");
            if (request.ProductId != null && request.MaterialId != null)
                throw new Exception("Chỉ được chọn 1 trong 2: ProductId hoặc MaterialId.");

            string itemName;
            string itemSku = "";
            string itemType;

            if (request.ProductId != null)
            {
                var product = await _context.Products.FindAsync(request.ProductId);
                if (product == null) throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
                itemName = product.Name;
                itemSku = product.Sku;
                itemType = "Product";
            }
            else
            {
                var material = await _context.Materials.FindAsync(request.MaterialId);
                if (material == null) throw new KeyNotFoundException("Không tìm thấy nguyên liệu.");
                itemName = material.Name;
                itemType = "Material";
            }

            var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == request.WarehouseId);
            if (!warehouseExists) throw new KeyNotFoundException("Không tìm thấy kho.");

            // Người dùng không còn chọn vị trí lưu trữ nữa -> tự lấy (hoặc tạo nếu chưa có) vị trí
            // "Normal" mặc định của kho, cùng ý tưởng với GoodsReceiptService khi nhận hàng theo PO.
            var location = await _context.WarehouseLocations
                .FirstOrDefaultAsync(l => l.WarehouseId == request.WarehouseId && l.Type == "Normal");
            if (location == null)
            {
                location = new WarehouseLocation
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = request.WarehouseId,
                    Name = "Vị trí mặc định",
                    Type = "Normal"
                };
                _context.WarehouseLocations.Add(location);
            }

            // Check if inventory already exists
            Inventory? existingInventory;
            if (request.ProductId != null)
            {
                existingInventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.ProductId == request.ProductId && i.WarehouseLocationId == location.Id);
            }
            else
            {
                existingInventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.MaterialId == request.MaterialId && i.WarehouseLocationId == location.Id);
            }

            if (existingInventory != null)
            {
                throw new InvalidOperationException("Mục này đã tồn tại trong kho này. Vui lòng cập nhật số lượng thay vì thêm mới.");
            }

            if (request.InitialQuantity < 0) throw new Exception("Số lượng ban đầu không hợp lệ.");

            var newInventory = new Inventory
            {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                MaterialId = request.MaterialId,
                WarehouseLocationId = location.Id,
                OnHandQuantity = request.InitialQuantity,
                ReservedQuantity = 0,
                AllocatedQuantity = 0,
                DamagedQuantity = 0,
                QuarantineQuantity = 0,
                InTransitQuantity = 0,
                LastUpdatedByUserId = staffId,
                LastUpdatedAt = DateTime.UtcNow
            };

            _context.Inventories.Add(newInventory);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Lưới an toàn cuối cho race check-then-insert: 2 request đồng thời cùng thêm 1 sản phẩm/
                // nguyên liệu vào cùng 1 vị trí kho -> request thứ 2 vi phạm unique index (Inventories) thay
                // vì tạo dòng trùng. Message giữ giống hệt case check ở trên để client xử lý nhất quán.
                throw new InvalidOperationException("Mục này đã tồn tại trong kho này. Vui lòng cập nhật số lượng thay vì thêm mới.");
            }

            var staff = await _context.Users.FindAsync(staffId);
            var staffName = staff?.FullName ?? "Nhân viên";
            var message = $"[Nhập Kho Mới] {staffName} vừa thêm {request.InitialQuantity} '{itemName}' vào vị trí '{location.Name}' (Kho {location.Warehouse?.Name}).";

            try
            {
                await _warehouseHub.Clients.All.SendAsync("ReceiveNotification", message);
            }
            catch (Exception ex)
            {
                // Tồn kho đã tạo và commit thành công ở trên -> lỗi gửi SignalR không được làm request
                // báo lỗi cho client, chỉ log để theo dõi.
                _logger.LogError(ex, "Lỗi gửi thông báo SignalR sau khi thêm Inventory {InventoryId}", newInventory.Id);
            }

            return new InventoryItemDto
            {
                Id = newInventory.Id,
                ProductId = newInventory.ProductId,
                MaterialId = newInventory.MaterialId,
                ItemName = itemName,
                ItemSku = itemSku,
                ItemType = itemType,
                OnHandQuantity = newInventory.OnHandQuantity,
                ReservedQuantity = newInventory.ReservedQuantity,
                AvailableQuantity = newInventory.AvailableQuantity,
                LastUpdatedByUserId = newInventory.LastUpdatedByUserId,
                LastUpdatedByUserName = staff?.FullName,
                LastUpdatedAt = newInventory.LastUpdatedAt
            };
        }

        // SQL Server error 2601 (unique index) / 2627 (unique constraint) — dùng để phân biệt vi phạm
        // unique index (Inventories) khỏi các lỗi DbUpdateException khác không nên bị nuốt thành 409.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

        public async Task<InventoryReportDto> GetInventoryReportAsync(Guid? warehouseId, DateTime? fromDate, DateTime? toDate)
        {
            // FE gửi toDate dạng chỉ-có-ngày (vd "2026-08-15" -> parse thành 00:00:00 UTC). Nếu dùng
            // thẳng làm mốc so sánh "<= to" thì mọi StockTransaction phát sinh sau nửa đêm hôm đó (tức
            // hầu như toàn bộ giao dịch trong ngày) sẽ bị loại khỏi biểu đồ -> "hôm nay" luôn ra 0.
            // Đẩy mốc kết thúc tới cuối ngày đó, giống cách GetInventoryByWarehouseAsync đã xử lý.
            var to = toDate.HasValue ? toDate.Value.Date.AddDays(1).AddTicks(-1) : DateTime.UtcNow;
            var from = fromDate ?? to.AddDays(-30);
            if (from > to)
                throw new ArgumentException("Khoảng thời gian không hợp lệ: 'fromDate' phải nhỏ hơn hoặc bằng 'toDate'.");

            var inventoryQuery = _context.Inventories
                .AsNoTracking()
                .Include(i => i.Product).ThenInclude(p => p!.Category)
                .Include(i => i.Material)
                .Include(i => i.WarehouseLocation)
                .AsQueryable();

            if (warehouseId.HasValue)
                inventoryQuery = inventoryQuery.Where(i => i.WarehouseLocation.WarehouseId == warehouseId.Value);

            // AvailableQuantity/OnHandQuantity*Price là tính toán ở C# (không dịch được sang SQL cho phần
            // OnHandQuantity*StandardListedPrice do decimal*decimal? overload) -> nạp về memory rồi tổng hợp,
            // theo đúng pattern đã dùng ở CeoDashboardService/LowStockAlertJob.
            var inventories = await inventoryQuery.ToListAsync();

            var totalSkus = inventories.Where(i => i.ProductId != null).Select(i => i.ProductId).Distinct().Count()
                          + inventories.Where(i => i.MaterialId != null).Select(i => i.MaterialId).Distinct().Count();

            var totalInventoryValue = inventories
                .Where(i => i.ProductId != null && i.Product != null)
                .Sum(i => i.OnHandQuantity * i.Product!.StandardListedPrice);

            // Ngưỡng tồn thấp của Product nay tính GỘP theo tổng khả dụng mọi kho (Product.ReorderThreshold,
            // mirror Material) chứ không còn theo từng dòng Inventory riêng lẻ -> phải tính trước tổng khả
            // dụng theo ProductId rồi mọi dòng của SP đó dùng chung 1 kết quả, đúng theo LowStockAlertJob/
            // GetLowStockAlertsAsync (nguồn xác thực).
            var productAvailableMap = inventories
                .Where(i => i.ProductId != null)
                .GroupBy(i => i.ProductId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.AvailableQuantity));

            // "<=" khớp với ngưỡng đã dùng ở GetLowStockAlertsAsync (nguồn xác thực cho trang Cảnh
            // báo tồn thấp) và đúng với SRS FT-12 ("crossing to or below the threshold") — trước đây
            // dùng "<" cho Product nên một sản phẩm chạm đúng ngưỡng bị đếm khác nhau giữa 2 màn hình.
            bool IsLow(Inventory i) =>
                (i.ProductId != null && i.Product != null && i.Product.ReorderThreshold != null &&
                    productAvailableMap.TryGetValue(i.ProductId.Value, out var totalAvail) && totalAvail <= i.Product.ReorderThreshold.Value) ||
                (i.MaterialId != null && i.Material != null && i.AvailableQuantity <= i.Material.SafetyThreshold);

            var lowStockItems = inventories.Where(IsLow).ToList();

            var totalWarehouses = warehouseId.HasValue ? 1 : await _context.Warehouses.CountAsync();

            var categoryBreakdown = inventories
                .GroupBy(i => i.Product?.Category?.Name ?? (i.MaterialId != null ? "Nguyên vật liệu" : "Khác"))
                .Select(g => new CategoryStockDto
                {
                    CategoryName = g.Key,
                    ItemCount = g.Select(i => i.ProductId ?? i.MaterialId).Distinct().Count(),
                    TotalOnHand = g.Sum(i => i.OnHandQuantity),
                    TotalValue = g.Where(i => i.ProductId != null && i.Product != null).Sum(i => i.OnHandQuantity * i.Product!.StandardListedPrice)
                })
                .OrderByDescending(c => c.TotalValue)
                .ToList();

            var topLowStockItems = lowStockItems
                .OrderBy(i => i.AvailableQuantity)
                .Take(5)
                .Select(i => new InventoryItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ItemName = i.Product != null ? i.Product.Name : i.Material != null ? i.Material.Name : "N/A",
                    ItemSku = i.Product != null ? i.Product.Sku : "",
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    Unit = i.Product != null ? i.Product.Unit : i.Material != null ? i.Material.Unit : "",
                    OnHandQuantity = i.OnHandQuantity,
                    ReservedQuantity = i.ReservedQuantity,
                    AvailableQuantity = i.AvailableQuantity,
                    LastUpdatedByUserId = i.LastUpdatedByUserId,
                    LastUpdatedAt = i.LastUpdatedAt
                })
                .ToList();

            var txQuery = _context.StockTransactions
                .AsNoTracking()
                .Where(t => t.CreatedAt >= from && t.CreatedAt <= to);

            if (warehouseId.HasValue)
                txQuery = txQuery.Where(t => t.WarehouseLocation.WarehouseId == warehouseId.Value);

            var transactionsByDay = await txQuery
                .GroupBy(t => t.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalIn = g.Where(t => t.QuantityChange > 0).Sum(t => t.QuantityChange),
                    TotalOut = g.Where(t => t.QuantityChange < 0).Sum(t => -t.QuantityChange)
                })
                .ToDictionaryAsync(x => x.Date, x => x);

            var stockMovement = new List<StockMovementPointDto>();
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                transactionsByDay.TryGetValue(day, out var point);
                stockMovement.Add(new StockMovementPointDto
                {
                    Date = day,
                    TotalIn = point?.TotalIn ?? 0,
                    TotalOut = point?.TotalOut ?? 0
                });
            }

            // Khi xem báo cáo toàn công ty (không lọc theo kho), dùng thẳng số liệu từ
            // GetLowStockAlertsAsync (nguồn xác thực duy nhất, đã gộp cả Material qua mọi kho) để khớp
            // tuyệt đối với trang Cảnh báo tồn thấp. Khi lọc theo 1 kho cụ thể, phương thức đó không hỗ
            // trợ phạm vi từng kho nên vẫn phải tính cục bộ — nhưng nay đã dùng chung ngưỡng "<=".
            var lowStockCount = warehouseId.HasValue
                ? lowStockItems.Count
                : (await GetLowStockAlertsAsync()).Count;

            return new InventoryReportDto
            {
                TotalSkus = totalSkus,
                TotalInventoryValue = totalInventoryValue,
                TotalWarehouses = totalWarehouses,
                LowStockCount = lowStockCount,
                CategoryBreakdown = categoryBreakdown,
                StockMovement = stockMovement,
                TopLowStockItems = topLowStockItems
            };
        }

        public async Task<List<SlowMovingItemDto>> GetSlowMovingItemsAsync(Guid? warehouseId, int days)
        {
            if (days <= 0)
                throw new ArgumentException("Số ngày phải lớn hơn 0.");

            var cutoff = DateTime.UtcNow.AddDays(-days);

            // Chỉ xét các mã còn tồn kho vật lý > 0 -> hết hàng không phải "chậm luân chuyển", đó là hết hàng.
            var inventoryQuery = _context.Inventories
                .AsNoTracking()
                .Include(i => i.Product)
                .Include(i => i.Material)
                .Include(i => i.WarehouseLocation)
                .Where(i => i.OnHandQuantity > 0);

            if (warehouseId.HasValue)
                inventoryQuery = inventoryQuery.Where(i => i.WarehouseLocation.WarehouseId == warehouseId.Value);

            var inventories = await inventoryQuery.ToListAsync();
            var inventoryIds = inventories.Select(i => i.Id).ToList();

            // "Xuất kho" = StockTransaction loại GoodsIssue làm giảm tồn (QuantityChange < 0); không tính
            // GoodsIssue dương (phiếu Reversal hoàn tồn) vì đó là nhập lại, không phải hàng rời kho.
            var lastOutboundMap = await _context.StockTransactions
                .AsNoTracking()
                .Where(t => t.TransactionType == TransactionType.GoodsIssue && t.QuantityChange < 0 && inventoryIds.Contains(t.InventoryId))
                .GroupBy(t => t.InventoryId)
                .Select(g => new { InventoryId = g.Key, LastOutboundAt = g.Max(t => t.CreatedAt) })
                .ToDictionaryAsync(x => x.InventoryId, x => x.LastOutboundAt);

            var now = DateTime.UtcNow;
            var result = new List<SlowMovingItemDto>();

            foreach (var inv in inventories)
            {
                var hasLast = lastOutboundMap.TryGetValue(inv.Id, out var lastOutboundAt);
                if (hasLast && lastOutboundAt >= cutoff) continue; // vừa xuất gần đây -> không phải hàng chậm luân chuyển

                var isMaterial = inv.MaterialId != null;
                int? daysSince = hasLast ? (int)Math.Floor((now - lastOutboundAt).TotalDays) : null;

                result.Add(new SlowMovingItemDto
                {
                    Id = inv.Id,
                    ProductId = inv.ProductId,
                    MaterialId = inv.MaterialId,
                    ItemType = isMaterial ? "Material" : "Product",
                    Sku = inv.Product?.Sku ?? string.Empty,
                    ItemName = inv.Product?.Name ?? inv.Material?.Name ?? "N/A",
                    Unit = inv.Product?.Unit ?? inv.Material?.Unit ?? string.Empty,
                    OnHandQuantity = inv.OnHandQuantity,
                    LastOutboundAt = hasLast ? lastOutboundAt : null,
                    DaysSinceLastOutbound = daysSince,
                    Suggestion = BuildSlowMovingSuggestion(isMaterial, daysSince)
                });
            }

            return result.OrderByDescending(r => r.DaysSinceLastOutbound ?? int.MaxValue).ToList();
        }

        private static string BuildSlowMovingSuggestion(bool isMaterial, int? daysSinceLastOutbound)
        {
            var d = daysSinceLastOutbound ?? int.MaxValue;

            if (isMaterial)
            {
                if (d >= 60) return "Chuyển dùng cho đơn hàng khác";
                if (d >= 30) return "Xuất sử dụng nội bộ";
                if (d >= 14) return "Kiểm tra nhu cầu sản xuất";
                return "Theo dõi thêm";
            }

            if (d >= 60) return "Cân nhắc thanh lý hoặc tái chế";
            if (d >= 30) return "Báo Marketing xây dựng chiến dịch";
            if (d >= 14) return "Giảm giá khuyến mãi";
            return "Theo dõi thêm";
        }

        // L3-INV-06: cùng phép so sánh ngưỡng với LowStockAlertJob (Product.ReorderThreshold gộp theo
        // tổng Inventories cho hàng thành phẩm, Material.SafetyThreshold tính từ tổng Inventories cho
        // nguyên vật liệu) — nhưng đọc trực tiếp, không gửi notification/không cooldown, phục vụ GET
        // theo yêu cầu.
        public async Task<List<LowStockAlertDto>> GetLowStockAlertsAsync()
        {
            var alerts = new List<LowStockAlertDto>();

            var candidateProducts = await _context.Products
                .Include(p => p.Inventories)
                .Where(p => p.ReorderThreshold != null)
                .ToListAsync();

            foreach (var product in candidateProducts)
            {
                var available = product.Inventories.Sum(i => i.AvailableQuantity);
                if (available > product.ReorderThreshold!.Value) continue;

                alerts.Add(new LowStockAlertDto
                {
                    ItemType = "Product",
                    ItemId = product.Id,
                    ItemName = product.Name,
                    ItemSku = product.Sku,
                    WarehouseId = null,
                    WarehouseName = null,
                    AvailableQuantity = available,
                    Threshold = product.ReorderThreshold.Value,
                    Unit = product.Unit,
                    SuggestedAction = "Tạo Purchase Order bổ sung hàng"
                });
            }

            var materials = await _context.Materials.Include(m => m.Inventories).ToListAsync();

            foreach (var material in materials)
            {
                var calculatedStock = material.Inventories.Any()
                    ? material.Inventories.Sum(i => i.AvailableQuantity)
                    : material.CurrentStock;

                if (calculatedStock > material.SafetyThreshold) continue;

                alerts.Add(new LowStockAlertDto
                {
                    ItemType = "Material",
                    ItemId = material.Id,
                    ItemName = material.Name,
                    ItemSku = null,
                    WarehouseId = null,
                    WarehouseName = null,
                    AvailableQuantity = calculatedStock,
                    Threshold = material.SafetyThreshold,
                    Unit = material.Unit,
                    SuggestedAction = "Đặt mua thêm nguyên vật liệu"
                });
            }

            return alerts;
        }

        // Mirror GetLowStockAlertsAsync theo chiều ngược lại: vượt ngưỡng TỐI ĐA (tồn đọng).
        public async Task<List<ExcessStockAlertDto>> GetExcessStockAlertsAsync()
        {
            var alerts = new List<ExcessStockAlertDto>();

            var candidateProducts = await _context.Products
                .Include(p => p.Inventories)
                .Where(p => p.ExcessThreshold != null)
                .ToListAsync();

            foreach (var product in candidateProducts)
            {
                var available = product.Inventories.Sum(i => i.AvailableQuantity);
                if (available <= product.ExcessThreshold!.Value) continue;

                alerts.Add(new ExcessStockAlertDto
                {
                    ItemType = "Product",
                    ItemId = product.Id,
                    ItemName = product.Name,
                    ItemSku = product.Sku,
                    AvailableQuantity = available,
                    Threshold = product.ExcessThreshold.Value,
                    Unit = product.Unit,
                    SuggestedAction = "Cân nhắc khuyến mãi hoặc tạm dừng nhập thêm"
                });
            }

            var materials = await _context.Materials.Include(m => m.Inventories).Where(m => m.MaxStockThreshold != null).ToListAsync();

            foreach (var material in materials)
            {
                var calculatedStock = material.Inventories.Any()
                    ? material.Inventories.Sum(i => i.AvailableQuantity)
                    : material.CurrentStock;

                if (calculatedStock <= material.MaxStockThreshold!.Value) continue;

                alerts.Add(new ExcessStockAlertDto
                {
                    ItemType = "Material",
                    ItemId = material.Id,
                    ItemName = material.Name,
                    ItemSku = null,
                    AvailableQuantity = calculatedStock,
                    Threshold = material.MaxStockThreshold.Value,
                    Unit = material.Unit,
                    SuggestedAction = "Tạm dừng đặt mua thêm nguyên vật liệu này"
                });
            }

            return alerts;
        }

    }
}
