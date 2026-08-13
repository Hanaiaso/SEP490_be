using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // DEF-L4-003: Phiên kiểm kê tồn kho theo Warehouse.
    // Mở phiên -> chốt (snapshot) số lý thuyết của mọi dòng tồn kho thuộc kho đó, khoá không đổi
    // trong lúc phiên mở. Đóng phiên ("post") -> dòng chênh lệch trong ngưỡng cấu hình được áp
    // dụng thẳng; dòng vượt ngưỡng tự tạo StockAdjustment (Pending) để CEO duyệt riêng, tái sử
    // dụng đúng luồng phê duyệt đã có ở StockAdjustmentService/CEOStockAdjustmentPage.
    public class InventoryCountSessionService : IInventoryCountSessionService
    {
        private const string VarianceThresholdConfigKey = "INVENTORY_COUNT_VARIANCE_THRESHOLD";
        private const int DefaultVarianceThreshold = 5;

        private readonly ApplicationDbContext _context;
        private readonly ISystemConfigService _systemConfigService;
        private readonly INotificationService _notificationService;

        public InventoryCountSessionService(
            ApplicationDbContext context,
            ISystemConfigService systemConfigService,
            INotificationService notificationService)
        {
            _context = context;
            _systemConfigService = systemConfigService;
            _notificationService = notificationService;
        }

        private static InventoryCountSessionItemDto ToItemDto(InventoryCountSessionItem item)
        {
            return new InventoryCountSessionItemDto
            {
                Id = item.Id,
                InventoryId = item.InventoryId,
                ItemName = item.Inventory.Product?.Name ?? item.Inventory.Material?.Name ?? "N/A",
                ItemSku = item.Inventory.Product?.Sku ?? string.Empty,
                SystemQuantity = item.SystemQuantity,
                PhysicalQuantity = item.PhysicalQuantity,
                Variance = item.Variance,
                Note = item.Note,
                AutoApplied = item.AutoApplied,
                StockAdjustmentId = item.StockAdjustmentId
            };
        }

        private static InventoryCountSessionDto ToDto(InventoryCountSession session)
        {
            return new InventoryCountSessionDto
            {
                Id = session.Id,
                WarehouseId = session.WarehouseId,
                WarehouseName = session.Warehouse?.Name ?? "N/A",
                Status = session.Status.ToString(),
                Note = session.Note,
                OpenedByUserId = session.OpenedByUserId,
                OpenedByName = session.OpenedByUser?.FullName ?? "N/A",
                OpenedAt = session.OpenedAt,
                ClosedByUserId = session.ClosedByUserId,
                ClosedByName = session.ClosedByUser?.FullName,
                ClosedAt = session.ClosedAt,
                Items = session.Items
                    .OrderBy(i => i.Inventory.Product != null ? i.Inventory.Product.Name : i.Inventory.Material!.Name)
                    .Select(ToItemDto)
                    .ToList()
            };
        }

        private IQueryable<InventoryCountSession> BaseQuery()
        {
            return _context.InventoryCountSessions
                .Include(s => s.Warehouse)
                .Include(s => s.OpenedByUser)
                .Include(s => s.ClosedByUser)
                .Include(s => s.Items).ThenInclude(i => i.Inventory).ThenInclude(inv => inv.Product)
                .Include(s => s.Items).ThenInclude(i => i.Inventory).ThenInclude(inv => inv.Material);
        }

        public async Task<List<InventoryCountSessionDto>> GetListAsync(Guid callerId, SystemRole callerRole, Guid? warehouseId, string? status)
        {
            var query = BaseQuery();

            // WarehouseStaff chỉ thấy phiên của kho mình được gán; CEO/Admin thấy mọi kho.
            if (callerRole == SystemRole.WarehouseStaff)
            {
                var staff = await _context.Users.FindAsync(callerId);
                query = query.Where(s => s.WarehouseId == (staff != null ? staff.AssignedWarehouseId : null));
            }

            if (warehouseId.HasValue)
            {
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<InventoryCountSessionStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(s => s.Status == parsedStatus);
            }

            var sessions = await query.OrderByDescending(s => s.OpenedAt).ToListAsync();
            return sessions.Select(ToDto).ToList();
        }

        public async Task<InventoryCountSessionDto> GetByIdAsync(Guid id)
        {
            var session = await BaseQuery().FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException("Không tìm thấy phiên kiểm kê.");
            return ToDto(session);
        }

        public async Task<InventoryCountSessionDto> OpenAsync(Guid staffId, OpenInventoryCountSessionRequest request)
        {
            var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId)
                ?? throw new KeyNotFoundException("Không tìm thấy kho.");

            var staff = await _context.Users.FindAsync(staffId);
            if (staff != null && staff.Role == SystemRole.WarehouseStaff && staff.AssignedWarehouseId != warehouse.Id)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền mở phiên kiểm kê cho kho này.");
            }

            var hasOpenSession = await _context.InventoryCountSessions
                .AnyAsync(s => s.WarehouseId == warehouse.Id && s.Status == InventoryCountSessionStatus.Open);
            if (hasOpenSession)
            {
                throw new InvalidOperationException("Kho này đang có một phiên kiểm kê chưa đóng. Hãy đóng phiên hiện tại trước khi mở phiên mới.");
            }

            // Chốt số lý thuyết = OnHandQuantity hiện tại của MỌI dòng tồn kho thuộc kho này.
            var inventories = await _context.Inventories
                .Where(i => i.WarehouseLocation.WarehouseId == warehouse.Id)
                .ToListAsync();

            if (inventories.Count == 0)
            {
                throw new InvalidOperationException("Kho này chưa có dòng tồn kho nào để kiểm kê.");
            }

            var session = new InventoryCountSession
            {
                WarehouseId = warehouse.Id,
                Status = InventoryCountSessionStatus.Open,
                Note = request.Note?.Trim(),
                OpenedByUserId = staffId,
                OpenedAt = DateTime.UtcNow,
                Items = inventories.Select(inv => new InventoryCountSessionItem
                {
                    InventoryId = inv.Id,
                    SystemQuantity = inv.OnHandQuantity,
                    PhysicalQuantity = null
                }).ToList()
            };

            _context.InventoryCountSessions.Add(session);
            await _context.SaveChangesAsync();

            return await GetByIdAsync(session.Id);
        }

        public async Task<InventoryCountSessionDto> RecordItemCountAsync(Guid sessionId, Guid itemId, RecordCountItemRequest request)
        {
            var session = await _context.InventoryCountSessions
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == sessionId)
                ?? throw new KeyNotFoundException("Không tìm thấy phiên kiểm kê.");

            if (session.Status != InventoryCountSessionStatus.Open)
            {
                throw new InvalidOperationException("Phiên kiểm kê đã đóng, không thể sửa số đếm.");
            }

            var item = session.Items.FirstOrDefault(i => i.Id == itemId)
                ?? throw new KeyNotFoundException("Không tìm thấy dòng kiểm kê này trong phiên.");

            item.PhysicalQuantity = request.PhysicalQuantity;
            item.Note = request.Note?.Trim();

            await _context.SaveChangesAsync();

            return await GetByIdAsync(sessionId);
        }

        private async Task<int> GetVarianceThresholdAsync()
        {
            var raw = await _systemConfigService.GetEffectiveValueAsync(VarianceThresholdConfigKey);
            return int.TryParse(raw, out var parsed) ? Math.Abs(parsed) : DefaultVarianceThreshold;
        }

        public async Task<CloseInventoryCountSessionResultDto> CloseAsync(Guid sessionId, Guid staffId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var session = await _context.InventoryCountSessions
                .Include(s => s.Items).ThenInclude(i => i.Inventory)
                .FirstOrDefaultAsync(s => s.Id == sessionId)
                ?? throw new KeyNotFoundException("Không tìm thấy phiên kiểm kê.");

            if (session.Status != InventoryCountSessionStatus.Open)
            {
                throw new InvalidOperationException("Phiên kiểm kê đã đóng trước đó.");
            }

            var uncounted = session.Items.Count(i => !i.PhysicalQuantity.HasValue);
            if (uncounted > 0)
            {
                throw new InvalidOperationException($"Còn {uncounted} dòng chưa nhập số đếm thực tế. Hãy đếm đủ trước khi đóng phiên.");
            }

            var threshold = await GetVarianceThresholdAsync();
            var autoAppliedCount = 0;
            var pendingApprovalCount = 0;

            foreach (var item in session.Items)
            {
                var variance = item.PhysicalQuantity!.Value - item.SystemQuantity;
                if (variance == 0) continue;

                if (Math.Abs(variance) <= threshold)
                {
                    // Trong ngưỡng -> áp dụng thẳng, không cần CEO duyệt.
                    var inventory = item.Inventory;
                    var newOnHand = inventory.OnHandQuantity + variance;
                    if (newOnHand < 0)
                    {
                        throw new InvalidOperationException(
                            $"Tồn kho hiện tại của '{inventory.Product?.Name ?? inventory.Material?.Name}' không đủ để áp dụng chênh lệch kiểm kê (đã có biến động khác kể từ lúc mở phiên).");
                    }

                    inventory.OnHandQuantity = newOnHand;
                    inventory.LastUpdatedByUserId = staffId;
                    inventory.LastUpdatedAt = DateTime.UtcNow;

                    _context.StockTransactions.Add(new StockTransaction
                    {
                        InventoryId = inventory.Id,
                        ProductId = inventory.ProductId,
                        MaterialId = inventory.MaterialId,
                        WarehouseLocationId = inventory.WarehouseLocationId,
                        QuantityChange = variance,
                        TransactionType = TransactionType.StockAdjustment,
                        ReferenceId = item.Id,
                        Note = $"Phiên kiểm kê {session.Id} — chênh lệch trong ngưỡng, áp dụng tự động.",
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow
                    });

                    item.AutoApplied = true;
                    autoAppliedCount++;
                }
                else
                {
                    // Vượt ngưỡng -> tạo đề xuất chờ CEO duyệt (tái sử dụng đúng entity/luồng StockAdjustment).
                    // Dùng SystemQuantity đã khoá từ lúc MỞ phiên, không snapshot lại theo tồn kho hiện tại.
                    var adjustment = new StockAdjustment
                    {
                        InventoryId = item.InventoryId,
                        SystemQuantity = item.SystemQuantity,
                        PhysicalQuantity = item.PhysicalQuantity.Value,
                        Variance = variance,
                        Reason = $"Phiên kiểm kê {session.Id} — chênh lệch {variance:+#;-#;0} vượt ngưỡng duyệt ({threshold}).",
                        Status = StockAdjustmentStatus.Pending,
                        ProposedByUserId = staffId,
                        ProposedAt = DateTime.UtcNow
                    };
                    _context.StockAdjustments.Add(adjustment);
                    await _context.SaveChangesAsync(); // cần Id trước khi gán vào item

                    item.StockAdjustmentId = adjustment.Id;
                    pendingApprovalCount++;
                }
            }

            session.Status = InventoryCountSessionStatus.Closed;
            session.ClosedByUserId = staffId;
            session.ClosedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            try
            {
                if (pendingApprovalCount > 0)
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_34_StockAdjustmentPendingApproval,
                        SystemRole.CEO,
                        "Đề xuất điều chỉnh tồn kho từ phiên kiểm kê",
                        $"Phiên kiểm kê vừa đóng tạo {pendingApprovalCount} đề xuất điều chỉnh tồn kho vượt ngưỡng, cần CEO xem xét.",
                        session.Id,
                        "InventoryCountSession"
                    );
                }

                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_39_InventoryCountSessionClosed,
                    staffId,
                    "Đã đóng phiên kiểm kê",
                    autoAppliedCount > 0 || pendingApprovalCount > 0
                        ? $"Phiên kiểm kê đã đóng: {autoAppliedCount} dòng áp dụng tự động, {pendingApprovalCount} dòng chờ CEO duyệt."
                        : "Phiên kiểm kê đã đóng: không có chênh lệch nào so với hệ thống.",
                    session.Id,
                    "InventoryCountSession"
                );
            }
            catch (Exception ex)
            {
                // Đã commit thành công -> lỗi gửi thông báo không được làm fail request, chỉ log.
                Console.WriteLine($"[InventoryCountSessionService] Error sending close notifications: {ex.Message}");
            }

            var dto = await GetByIdAsync(session.Id);
            return new CloseInventoryCountSessionResultDto
            {
                Session = dto,
                AutoAppliedCount = autoAppliedCount,
                PendingApprovalCount = pendingApprovalCount
            };
        }
    }
}
