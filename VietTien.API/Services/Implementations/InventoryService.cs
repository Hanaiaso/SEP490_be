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

        public InventoryService(ApplicationDbContext context, IHubContext<WarehouseHub> warehouseHub, ILogger<InventoryService> logger)
        {
            _context = context;
            _warehouseHub = warehouseHub;
            _logger = logger;
        }

        public async Task<PaginatedList<InventoryItemDto>> GetInventoryByWarehouseAsync(Guid warehouseId, string? search, int? minQty, int? maxQty, DateTime? fromDate, DateTime? toDate, int pageNumber, int pageSize)
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

            var staff = await _context.Users.FindAsync(staffId);
            if (staff != null && staff.Role == SystemRole.WarehouseStaff &&
                staff.AssignedWarehouseId != inventory.WarehouseLocation.WarehouseId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền điều chỉnh tồn kho của kho này.");
            }

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

            var location = await _context.WarehouseLocations.Include(l => l.Warehouse).FirstOrDefaultAsync(l => l.Id == request.WarehouseLocationId);
            if (location == null) throw new KeyNotFoundException("Không tìm thấy vị trí lưu trữ.");

            // Check if inventory already exists
            Inventory? existingInventory;
            if (request.ProductId != null)
            {
                existingInventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.ProductId == request.ProductId && i.WarehouseLocationId == request.WarehouseLocationId);
            }
            else
            {
                existingInventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.MaterialId == request.MaterialId && i.WarehouseLocationId == request.WarehouseLocationId);
            }

            if (existingInventory != null)
            {
                throw new InvalidOperationException("Mục này đã tồn tại ở vị trí lưu trữ này. Vui lòng cập nhật số lượng thay vì thêm mới.");
            }

            if (request.InitialQuantity < 0) throw new Exception("Số lượng ban đầu không hợp lệ.");

            var newInventory = new Inventory
            {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                MaterialId = request.MaterialId,
                WarehouseLocationId = request.WarehouseLocationId,
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
                throw new InvalidOperationException("Mục này đã tồn tại ở vị trí lưu trữ này. Vui lòng cập nhật số lượng thay vì thêm mới.");
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
            var to = toDate ?? DateTime.UtcNow;
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

            bool IsLow(Inventory i) =>
                (i.ProductId != null && i.ReorderThreshold != null && i.AvailableQuantity < i.ReorderThreshold.Value) ||
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

            return new InventoryReportDto
            {
                TotalSkus = totalSkus,
                TotalInventoryValue = totalInventoryValue,
                TotalWarehouses = totalWarehouses,
                LowStockCount = lowStockItems.Count,
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

        public async Task<ShiftInventoryCountResultDto> SubmitShiftInventoryCountAsync(ShiftInventoryCountRequestDto request, Guid staffId)
        {
            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Danh sách kiểm kê không được để trống.");

            if (request.Items.Select(i => i.InventoryId).Distinct().Count() != request.Items.Count)
                throw new ArgumentException("Danh sách kiểm kê có mã tồn kho bị trùng lặp.");

            // Cho phép trễ 1 ngày để bù lệch múi giờ giữa client/server, không cho kiểm kê cho ngày ở tương lai.
            if (request.CountDate.Date > DateTime.UtcNow.AddDays(1).Date)
                throw new ArgumentException("Ngày kiểm kê không được ở tương lai.");

            var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == request.WarehouseId);
            if (!warehouseExists)
                throw new KeyNotFoundException("Không tìm thấy kho đã chọn.");

            var staff = await _context.Users.FindAsync(staffId);

            var shiftLabel = "ca không xác định";
            if (request.ShiftId.HasValue)
            {
                var shift = await _context.WarehouseShifts.FindAsync(request.ShiftId.Value);
                if (shift != null)
                    shiftLabel = $"{shift.Name} ({shift.StartTime:hh\\:mm}-{shift.EndTime:hh\\:mm})";
            }

            var inventoryIds = request.Items.Select(i => i.InventoryId).ToList();
            var inventories = await _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.Material)
                .Include(i => i.WarehouseLocation)
                .Where(i => inventoryIds.Contains(i.Id))
                .ToListAsync();

            var result = new ShiftInventoryCountResultDto { TotalCounted = request.Items.Count };

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var itemReq in request.Items)
                {
                    var inv = inventories.FirstOrDefault(i => i.Id == itemReq.InventoryId);
                    if (inv == null) continue; // dòng không tồn tại -> bỏ qua, không chặn cả phiên kiểm kê

                    if (inv.WarehouseLocation.WarehouseId != request.WarehouseId)
                        continue; // an toàn: bỏ qua dòng không thuộc kho đã chọn trong request

                    if (staff != null && staff.Role == SystemRole.WarehouseStaff &&
                        staff.AssignedWarehouseId != inv.WarehouseLocation.WarehouseId)
                    {
                        throw new UnauthorizedAccessException("Bạn không có quyền kiểm kê tồn kho của kho này.");
                    }

                    var diff = itemReq.ActualQuantity - inv.OnHandQuantity;
                    if (diff == 0) continue; // khớp tồn hệ thống -> không cần ghi nhận điều chỉnh

                    var oldQuantity = inv.OnHandQuantity;
                    inv.OnHandQuantity = itemReq.ActualQuantity;
                    inv.LastUpdatedByUserId = staffId;
                    inv.LastUpdatedAt = DateTime.UtcNow;

                    var noteText = $"Kiểm kê {shiftLabel} ngày {request.CountDate:dd/MM/yyyy}";
                    if (!string.IsNullOrWhiteSpace(itemReq.Note))
                        noteText += $": {itemReq.Note.Trim()}";
                    if (noteText.Length > 500)
                        noteText = noteText.Substring(0, 500); // giới hạn theo cột Note (nvarchar(500))

                    _context.StockTransactions.Add(new StockTransaction
                    {
                        InventoryId = inv.Id,
                        ProductId = inv.ProductId,
                        MaterialId = inv.MaterialId,
                        WarehouseLocationId = inv.WarehouseLocationId,
                        QuantityChange = diff,
                        TransactionType = TransactionType.StockAdjustment,
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow,
                        Note = noteText
                    });

                    result.AdjustedItems.Add(new ShiftAdjustedItemDto
                    {
                        InventoryId = inv.Id,
                        ItemName = inv.Product?.Name ?? inv.Material?.Name ?? "N/A",
                        OldQuantity = oldQuantity,
                        NewQuantity = itemReq.ActualQuantity
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            result.AdjustedCount = result.AdjustedItems.Count;
            return result;
        }

        // L3-INV-06: cùng phép so sánh ngưỡng với LowStockAlertJob (Inventory.ReorderThreshold cho
        // hàng thành phẩm, Material.SafetyThreshold tính từ tổng Inventories cho nguyên vật liệu) —
        // nhưng đọc trực tiếp, không gửi notification/không cooldown, phục vụ GET theo yêu cầu.
        public async Task<List<LowStockAlertDto>> GetLowStockAlertsAsync()
        {
            var alerts = new List<LowStockAlertDto>();

            // AvailableQuantity là computed property phía C# (Math.Max(0, OnHand - Reserved - ...)),
            // KHÔNG map ra cột DB nào -> đưa thẳng vào .Where() sẽ bị EF cố dịch sang SQL và ném
            // InvalidOperationException (route trả 409 qua ExceptionHandlingMiddleware) khi chạy với
            // provider SQL Server thật (EF InMemory dùng trong unit test client-eval nên không phát
            // hiện được lỗi này). Chỉ lọc ReorderThreshold != null (cột thật) ở SQL, còn so sánh với
            // AvailableQuantity phải làm SAU khi đã ToListAsync() — đúng như LowStockAlertJob đang làm.
            var candidateInventories = await _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.WarehouseLocation).ThenInclude(wl => wl.Warehouse)
                .Where(i => i.ReorderThreshold != null)
                .ToListAsync();
            var lowStockInventories = candidateInventories
                .Where(i => i.AvailableQuantity <= i.ReorderThreshold!.Value)
                .ToList();

            alerts.AddRange(lowStockInventories.Select(inv => new LowStockAlertDto
            {
                ItemType = "Product",
                ItemId = inv.ProductId ?? inv.Id,
                ItemName = inv.Product?.Name ?? "(Sản phẩm không xác định)",
                ItemSku = inv.Product?.Sku,
                WarehouseId = inv.WarehouseLocation?.WarehouseId,
                WarehouseName = inv.WarehouseLocation?.Warehouse?.Name,
                AvailableQuantity = inv.AvailableQuantity,
                Threshold = inv.ReorderThreshold!.Value,
                SuggestedAction = "Tạo Purchase Order bổ sung hàng"
            }));

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
                    SuggestedAction = "Đặt mua thêm nguyên vật liệu"
                });
            }

            return alerts;
        }

        // ─── INV-01: Kiểm kê kho 2 bước — snapshot lý thuyết -> ghi số đếm thực tế ─────────────
        // Khác với SubmitShiftInventoryCountAsync (ghi đè OnHandQuantity ngay lập tức), luồng này
        // KHÔNG đụng tới Inventory.OnHandQuantity ở bước ghi số đếm — chỉ lưu song song để đối chiếu.

        private static StockCountSessionDto ToSessionDto(StockCountSession session)
        {
            return new StockCountSessionDto
            {
                Id = session.Id,
                WarehouseId = session.WarehouseId,
                Status = session.Status.ToString(),
                CreatedByUserId = session.CreatedByUserId,
                CreatedAt = session.CreatedAt,
                TheoreticalLockedAt = session.TheoreticalLockedAt,
                Lines = session.Lines?.Select(l => new InventoryCountLineDto
                {
                    Id = l.Id,
                    InventoryId = l.InventoryId,
                    ItemName = l.Inventory?.Product?.Name ?? l.Inventory?.Material?.Name ?? "N/A",
                    TheoreticalQuantity = l.TheoreticalQuantity,
                    ActualQuantity = l.ActualQuantity,
                    CountedAt = l.CountedAt
                }).ToList() ?? new List<InventoryCountLineDto>()
            };
        }

        private async Task<StockCountSession> LoadSessionAsync(Guid sessionId)
        {
            var session = await _context.StockCountSessions
                .Include(s => s.Lines).ThenInclude(l => l.Inventory).ThenInclude(i => i.Product)
                .Include(s => s.Lines).ThenInclude(l => l.Inventory).ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            return session ?? throw new KeyNotFoundException("Không tìm thấy phiên kiểm kê.");
        }

        public async Task<StockCountSessionDto> CreateCountSessionAsync(Guid staffId, Guid warehouseId)
        {
            var warehouseExists = await _context.Warehouses.AnyAsync(w => w.Id == warehouseId);
            if (!warehouseExists)
                throw new KeyNotFoundException("Không tìm thấy kho đã chọn.");

            var session = new StockCountSession
            {
                WarehouseId = warehouseId,
                Status = CountSessionStatus.Draft,
                CreatedByUserId = staffId,
                CreatedAt = DateTime.UtcNow
            };
            _context.StockCountSessions.Add(session);
            await _context.SaveChangesAsync();

            return ToSessionDto(session);
        }

        public async Task<StockCountSessionDto> LockTheoreticalAsync(Guid sessionId)
        {
            var session = await LoadSessionAsync(sessionId);

            if (session.Status != CountSessionStatus.Draft)
                throw new CountSnapshotStateInvalidException("Phiên kiểm kê đã được khóa snapshot hoặc đã hoàn tất, không thể khóa lại.");

            var inventories = await _context.Inventories
                .Where(i => i.WarehouseLocation.WarehouseId == session.WarehouseId)
                .ToListAsync();

            foreach (var inv in inventories)
            {
                _context.StockCountLines.Add(new StockCountLine
                {
                    StockCountSessionId = session.Id,
                    InventoryId = inv.Id,
                    TheoreticalQuantity = inv.OnHandQuantity
                });
            }

            session.Status = CountSessionStatus.TheoreticalLocked;
            session.TheoreticalLockedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Load lại: các StockCountLine vừa thêm được EF fixup vào session.Lines, nhưng
            // Inventory.Product/Material của chúng chưa Include -> ItemName sẽ rỗng nếu dùng thẳng session hiện tại.
            return ToSessionDto(await LoadSessionAsync(session.Id));
        }

        public async Task<StockCountSessionDto> RecordCountLineAsync(Guid sessionId, RecordCountLineRequestDto dto)
        {
            if (dto.ActualQuantity < 0)
                throw new ArgumentException("Số lượng đếm thực tế phải lớn hơn hoặc bằng 0.");

            var session = await LoadSessionAsync(sessionId);

            var line = session.Lines.FirstOrDefault(l => l.InventoryId == dto.InventoryId)
                ?? throw new KeyNotFoundException("Dòng tồn kho này không nằm trong snapshot của phiên kiểm kê.");

            line.ActualQuantity = dto.ActualQuantity;
            line.CountedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return ToSessionDto(session);
        }
    }
}
