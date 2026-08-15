using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/warehouse-management")]
    [ApiController]
    public class WarehouseManagementController : ControllerBase
    {
        private readonly IWarehouseManagementService _service;
        private readonly ApplicationDbContext _context;
        private readonly IWarehouseAccessGuard _warehouseAccessGuard;

        public WarehouseManagementController(
            IWarehouseManagementService service,
            ApplicationDbContext context,
            IWarehouseAccessGuard warehouseAccessGuard)
        {
            _service = service;
            _context = context;
            _warehouseAccessGuard = warehouseAccessGuard;
        }

        private Guid GetUserId()
        {
            var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(v) || !Guid.TryParse(v, out var id))
                throw new UnauthorizedAccessException("Invalid user token.");
            return id;
        }

        /// <summary>
        /// Id các vị trí lưu trữ thuộc một kho. Lọc tồn kho qua Inventory.WarehouseLocationId (khoá
        /// ngoại vô hướng) thay vì đi qua navigation Inventory.WarehouseLocation: dữ liệu cũ có dòng
        /// tồn kho không trỏ tới vị trí hợp lệ, và bám navigation ở đó sẽ âm thầm loại mất bản ghi.
        /// </summary>
        private IQueryable<Guid> LocationIdsOfWarehouse(Guid warehouseId)
            => _context.WarehouseLocations.Where(l => l.WarehouseId == warehouseId).Select(l => l.Id);

        /// <summary>Kho chứa dòng tồn kho này, null nếu dữ liệu cũ không trỏ tới vị trí hợp lệ.</summary>
        private async Task<Guid?> ResolveWarehouseIdAsync(Guid warehouseLocationId)
            => await _context.WarehouseLocations
                .Where(l => l.Id == warehouseLocationId)
                .Select(l => (Guid?)l.WarehouseId)
                .FirstOrDefaultAsync();

        [HttpGet]
        [Authorize(Roles = "CEO,WarehouseStaff,Admin")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var result = await _service.GetAllWarehousesAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "CEO,WarehouseStaff,Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _service.GetWarehouseByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _service.CreateWarehouseAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWarehouseDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var result = await _service.UpdateWarehouseAsync(id, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "CEO,Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _service.DeleteWarehouseAsync(id);
                return Ok(new { message = "Xóa kho thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        // =====================================================================
        // LUỒNG 5 – BƯỚC 5: QUARANTINE (ĐỔI TRẢ HÀNG LỖI)
        // =====================================================================

        /// <summary>Lấy danh sách hàng đang cách ly</summary>
        [HttpGet("quarantine")]
        [Authorize(Roles = "WarehouseStaff,SalesManager,Admin")]
        public async Task<ActionResult<List<QuarantineListItemDto>>> GetQuarantineList()
        {
            try
            {
                var query = _context.QuarantineLogs
                    .Include(q => q.Order)
                    .Include(q => q.Product)
                    .Include(q => q.ReceivedByUser)
                    .Include(q => q.DispatchedByUser)
                    .Include(q => q.Product)
                    .Include(q => q.Material)
                    .AsQueryable();

                // Trước đây danh sách trả về toàn bộ lô cách ly của mọi kho. WarehouseStaff chỉ được
                // thấy lô thuộc kho được phân công; SalesManager/Admin vẫn xem xuyên kho để duyệt.
                var scopedWarehouseId = await _warehouseAccessGuard.GetScopedWarehouseIdAsync(GetUserId());
                if (scopedWarehouseId.HasValue)
                {
                    var locationIds = LocationIdsOfWarehouse(scopedWarehouseId.Value);
                    query = query.Where(q =>
                        q.Inventory != null && locationIds.Contains(q.Inventory.WarehouseLocationId));
                }

                var items = await query
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => new QuarantineListItemDto
                    {
                        Id = q.Id,
                        QuarantineCode = q.QuarantineCode,
                        OrderId = q.OrderId,
                        OrderCode = q.Order != null ? q.Order.OrderCode : null,
                        ProductId = q.ProductId,
                        MaterialId = q.MaterialId,
                        ItemName = q.Product != null ? q.Product.Name : q.Material != null ? q.Material.Name : "N/A",
                        ItemSku = q.Product != null ? q.Product.Sku : "",
                        ItemType = q.MaterialId != null ? "Material" : "Product",
                        Quantity = q.Quantity,
                        Reason = q.Reason,
                        Status = q.Status.ToString(),
                        ReceivedByName = q.ReceivedByUser.FullName,
                        CreatedAt = q.CreatedAt,
                        DispatchedAction = q.Status == QuarantineStatus.ApprovedAvailable ? "available"
                                         : q.Status == QuarantineStatus.ApprovedDamaged ? "damaged"
                                         : null,
                        DispatchedByName = q.DispatchedByUser != null ? q.DispatchedByUser.FullName : null,
                        DispatchedAt = q.DispatchedAt
                    })
                    .ToListAsync();

                return Ok(items);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Kho nhập hàng lỗi đổi trả vào vùng cách ly Quarantine (Optimistic Concurrency)</summary>
        [HttpPost("quarantine/receive")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> ReceiveToQuarantine([FromBody] QuarantineReceiveDto dto)
        {
            try
            {
                var userId = GetUserId();

                // Trước đây truy vấn này bỏ qua warehouse hoàn toàn (lấy dòng tồn kho ĐẦU TIÊN của
                // sản phẩm ở bất kỳ kho nào) -> hàng cách ly có thể bị cộng vào kho khác. Giới hạn
                // theo kho được phân công của người tiếp nhận.
                var scopedWarehouseId = await _warehouseAccessGuard.GetScopedWarehouseIdAsync(userId);

                var inventoryQuery = _context.Inventories
                    .Where(i => i.ProductId == dto.ProductId);

                if (scopedWarehouseId.HasValue)
                {
                    var locationIds = LocationIdsOfWarehouse(scopedWarehouseId.Value);
                    inventoryQuery = inventoryQuery.Where(i => locationIds.Contains(i.WarehouseLocationId));
                }

                var inventory = await inventoryQuery.FirstOrDefaultAsync();

                if (inventory == null)
                    return NotFound(new { message = "Không tìm thấy dòng tồn kho của sản phẩm này trong kho bạn phụ trách." });

                // Đảm bảo order tồn tại
                var order = await _context.Orders.FindAsync(dto.OrderId);
                if (order == null) return NotFound(new { message = "Không tìm thấy đơn hàng." });

                // Sinh mã cách ly
                var quarantineCode = $"QZ-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";

                // Nhập vào QuarantineQuantity (trừ AvailableQuantity gián tiếp qua OnHand)
                inventory.QuarantineQuantity += dto.Quantity;

                var log = new QuarantineLog
                {
                    QuarantineCode = quarantineCode,
                    OrderId = dto.OrderId,
                    ProductId = dto.ProductId,
                    InventoryId = inventory.Id,
                    Quantity = dto.Quantity,
                    Reason = dto.Reason,
                    Status = QuarantineStatus.Waiting,
                    ReceivedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.QuarantineLogs.AddAsync(log);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Conflict(new { message = "Dữ liệu tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
                }

                return Ok(new { quarantineCode, message = $"Đã nhập {dto.Quantity} đơn vị vào khu cách ly {quarantineCode}." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// QA xét duyệt hàng cách ly (cần quyền quản lý).
        /// Nếu 'available': chuyển về Available (giảm QuarantineQty).
        /// Nếu 'damaged': chuyển sang Damaged (tăng DamagedQty, giảm QuarantineQty).
        /// </summary>
        [HttpPost("quarantine/{id:guid}/dispatch")]
        [Authorize(Roles = "SalesManager,WarehouseStaff,Admin")]
        public async Task<IActionResult> DispatchQuarantine(Guid id, [FromBody] QuarantineDispatchDto dto)
        {
            try
            {
                var userId = GetUserId();

                var log = await _context.QuarantineLogs
                    .Include(q => q.Inventory)
                    .FirstOrDefaultAsync(q => q.Id == id);

                if (log == null) return NotFound(new { message = "Không tìm thấy bản ghi cách ly." });
                if (log.Status != QuarantineStatus.Waiting)
                    return BadRequest(new { message = "Bản ghi này đã được xử lý trước đó." });

                // Duyệt lô cách ly làm thay đổi QuarantineQuantity/DamagedQuantity của tồn kho thật,
                // nên WarehouseStaff chỉ được duyệt lô thuộc kho mình phụ trách.
                if (log.Inventory != null)
                {
                    var logWarehouseId = await ResolveWarehouseIdAsync(log.Inventory.WarehouseLocationId);
                    if (logWarehouseId.HasValue)
                    {
                        await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                            userId, logWarehouseId.Value,
                            "duyệt lô hàng cách ly", "QuarantineLog", log.Id.ToString());
                    }
                }

                var scopedWarehouseId = await _warehouseAccessGuard.GetScopedWarehouseIdAsync(userId);
                var action = dto.Action?.ToLower() ?? "available";
                var inventory = log.Inventory;

                if (inventory == null)
                {
                    // Auto-healing cho dữ liệu cũ (chưa có liên kết Inventory) — vẫn phải bám đúng
                    // kho được phân công, nếu không thì nhánh này lại thành đường vòng qua guard.
                    var healQuery = _context.Inventories
                        .Where(i => log.ProductId != null
                            ? i.ProductId == log.ProductId
                            : i.MaterialId == log.MaterialId);

                    if (scopedWarehouseId.HasValue)
                    {
                        var locationIds = LocationIdsOfWarehouse(scopedWarehouseId.Value);
                        healQuery = healQuery.Where(i => locationIds.Contains(i.WarehouseLocationId));
                    }

                    inventory = await healQuery.FirstOrDefaultAsync();

                    if (inventory != null)
                    {
                        log.InventoryId = inventory.Id;
                        log.Inventory = inventory;
                        // Phục hồi lại QuarantineQuantity vì dữ liệu cũ lúc nhập chưa được cộng
                        inventory.QuarantineQuantity += log.Quantity;
                    }
                    else
                    {
                        return BadRequest(new { message = "Lỗi dữ liệu: Bản ghi cách ly này không được liên kết với tồn kho (Inventory). Vui lòng kiểm tra lại dữ liệu cũ." });
                    }
                }

                if (action == "available")
                {
                    // Hàng đạt → giảm QuarantineQuantity (tự động AvailableQuantity tăng)
                    inventory.QuarantineQuantity = Math.Max(0, inventory.QuarantineQuantity - log.Quantity);
                    log.Status = QuarantineStatus.ApprovedAvailable;
                }
                else if (action == "damaged")
                {
                    // Hàng hỏng → chuyển sang DamagedQuantity
                    inventory.QuarantineQuantity = Math.Max(0, inventory.QuarantineQuantity - log.Quantity);
                    inventory.DamagedQuantity += log.Quantity;
                    log.Status = QuarantineStatus.ApprovedDamaged;
                }
                else
                {
                    return BadRequest(new { message = "Action không hợp lệ. Dùng 'available' hoặc 'damaged'." });
                }

                log.DispatchedByUserId = userId;
                log.DispatchedAt = DateTime.UtcNow;
                log.DispatchNotes = dto.Notes;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Conflict(new { message = "Dữ liệu tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
                }

                var successMsg = action == "available"
                    ? $"Duyệt thành công: {log.Quantity} đơn vị đã được chuyển về kho khả dụng."
                    : $"Duyệt thành công: {log.Quantity} đơn vị đã được chuyển vào kho hư hỏng (Damaged).";

                return Ok(new { message = successMsg });
                }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        
        [HttpGet("staff")]
        [Authorize]
        public async Task<IActionResult> GetStaff([FromServices] Data.ApplicationDbContext context)
        {
            try
            {
                // Fetch staff users (WarehouseStaff, CEO, Admin) or all users for demo
                var staffRoles = new[] { Models.SystemRole.WarehouseStaff, Models.SystemRole.CEO, Models.SystemRole.Admin };
                var users = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    System.Linq.Queryable.Where(context.Users, u => staffRoles.Contains(u.Role))
                );

                var result = System.Linq.Enumerable.Select(users, u => new {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.PhoneNumber,
                    Role = u.Role.ToString()
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
