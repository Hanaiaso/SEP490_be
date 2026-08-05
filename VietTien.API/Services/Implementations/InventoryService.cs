using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
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

            var oldQuantity = inventory.OnHandQuantity;
            inventory.OnHandQuantity = newQuantity;
            inventory.LastUpdatedByUserId = staffId;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            // In a real system, you might want to log the `note` to an Audit table
            // For now, we update the inventory directly per requirements

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
    }
}
