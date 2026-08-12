using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // P0-1: Kiểm kê kho -> CEO duyệt điều chỉnh tồn kho.
    // Warehouse Staff tạo đề xuất (Pending) sau khi kiểm kê thực tế; CEO duyệt/từ chối.
    // Chỉ khi Approve mới thực sự cập nhật Inventory.OnHandQuantity + ghi StockTransaction.
    public class StockAdjustmentService : IStockAdjustmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public StockAdjustmentService(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        private static StockAdjustmentDto ToDto(StockAdjustment a)
        {
            return new StockAdjustmentDto
            {
                Id = a.Id,
                InventoryId = a.InventoryId,
                ItemName = a.Inventory.Product?.Name ?? a.Inventory.Material?.Name ?? "N/A",
                ItemSku = a.Inventory.Product?.Sku ?? string.Empty,
                WarehouseName = a.Inventory.WarehouseLocation?.Warehouse?.Name ?? "N/A",
                SystemQuantity = a.SystemQuantity,
                PhysicalQuantity = a.PhysicalQuantity,
                Variance = a.Variance,
                Reason = a.Reason,
                Status = a.Status.ToString(),
                ProposedByUserId = a.ProposedByUserId,
                ProposedByName = a.ProposedByUser?.FullName ?? "N/A",
                ProposedAt = a.ProposedAt,
                DecidedByUserId = a.DecidedByUserId,
                DecidedByName = a.DecidedByUser?.FullName,
                DecidedAt = a.DecidedAt,
                DecisionNote = a.DecisionNote
            };
        }

        private IQueryable<StockAdjustment> BaseQuery()
        {
            return _context.StockAdjustments
                .Include(a => a.Inventory).ThenInclude(i => i.Product)
                .Include(a => a.Inventory).ThenInclude(i => i.Material)
                .Include(a => a.Inventory).ThenInclude(i => i.WarehouseLocation).ThenInclude(wl => wl.Warehouse)
                .Include(a => a.ProposedByUser)
                .Include(a => a.DecidedByUser);
        }

        public async Task<List<StockAdjustmentDto>> GetListAsync(Guid callerId, SystemRole callerRole, string? status)
        {
            var query = BaseQuery();

            // WarehouseStaff chỉ thấy đề xuất của chính mình; CEO/Admin thấy tất cả.
            if (callerRole == SystemRole.WarehouseStaff)
            {
                query = query.Where(a => a.ProposedByUserId == callerId);
            }

            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<StockAdjustmentStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(a => a.Status == parsedStatus);
            }

            var items = await query
                .OrderByDescending(a => a.ProposedAt)
                .ToListAsync();

            return items.Select(ToDto).ToList();
        }

        public async Task<StockAdjustmentDto> GetByIdAsync(Guid id)
        {
            var adjustment = await BaseQuery().FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new KeyNotFoundException("Không tìm thấy đề xuất điều chỉnh tồn kho.");
            return ToDto(adjustment);
        }

        public async Task<StockAdjustmentDto> CreateAsync(Guid staffId, CreateStockAdjustmentRequest request)
        {
            var inventory = await _context.Inventories
                .Include(i => i.Product)
                .Include(i => i.Material)
                .Include(i => i.WarehouseLocation).ThenInclude(wl => wl.Warehouse)
                .FirstOrDefaultAsync(i => i.Id == request.InventoryId)
                ?? throw new KeyNotFoundException("Không tìm thấy dòng tồn kho.");

            var staff = await _context.Users.FindAsync(staffId);
            if (staff != null && staff.Role == SystemRole.WarehouseStaff &&
                staff.AssignedWarehouseId != inventory.WarehouseLocation.WarehouseId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền đề xuất điều chỉnh tồn kho của kho này.");
            }

            var systemQty = inventory.OnHandQuantity;
            var adjustment = new StockAdjustment
            {
                InventoryId = inventory.Id,
                SystemQuantity = systemQty,
                PhysicalQuantity = request.PhysicalQuantity,
                Variance = request.PhysicalQuantity - systemQty,
                Reason = request.Reason.Trim(),
                Status = StockAdjustmentStatus.Pending,
                ProposedByUserId = staffId,
                ProposedAt = DateTime.UtcNow
            };

            _context.StockAdjustments.Add(adjustment);
            await _context.SaveChangesAsync();

            var itemName = inventory.Product?.Name ?? inventory.Material?.Name ?? "N/A";
            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_34_StockAdjustmentPendingApproval,
                    SystemRole.CEO,
                    "Đề xuất điều chỉnh tồn kho mới",
                    $"{staff?.FullName ?? "Nhân viên kho"} vừa đề xuất điều chỉnh tồn kho cho '{itemName}' (chênh lệch {adjustment.Variance:+#;-#;0}). Cần CEO xem xét.",
                    adjustment.Id,
                    "StockAdjustment"
                );
            }
            catch (Exception ex)
            {
                // Đề xuất đã lưu thành công -> lỗi gửi thông báo không được làm fail request, chỉ log.
                Console.WriteLine($"[StockAdjustmentService] Error sending pending-approval notification: {ex.Message}");
            }

            return await GetByIdAsync(adjustment.Id);
        }

        public async Task<StockAdjustmentDto> DecideAsync(Guid adjustmentId, Guid ceoId, StockAdjustmentDecisionRequest request)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var adjustment = await _context.StockAdjustments
                .Include(a => a.Inventory)
                .FirstOrDefaultAsync(a => a.Id == adjustmentId)
                ?? throw new KeyNotFoundException("Không tìm thấy đề xuất điều chỉnh tồn kho.");

            if (adjustment.Status != StockAdjustmentStatus.Pending)
            {
                throw new InvalidOperationException("Đề xuất này đã được xử lý, không thể duyệt lại.");
            }

            if (request.Decision == "Rejected")
            {
                if (string.IsNullOrWhiteSpace(request.Note))
                {
                    throw new InvalidOperationException("Từ chối đề xuất bắt buộc phải có ghi chú lý do.");
                }

                adjustment.Status = StockAdjustmentStatus.Rejected;
            }
            else
            {
                var inventory = adjustment.Inventory;
                var newOnHand = inventory.OnHandQuantity + adjustment.Variance;

                if (newOnHand < 0)
                {
                    throw new InvalidOperationException(
                        "Tồn kho hiện tại không đủ để áp dụng điều chỉnh này (đã có biến động khác kể từ lúc đề xuất). Vui lòng kiểm tra lại.");
                }

                inventory.OnHandQuantity = newOnHand;
                inventory.LastUpdatedByUserId = ceoId;
                inventory.LastUpdatedAt = DateTime.UtcNow;

                _context.StockTransactions.Add(new StockTransaction
                {
                    InventoryId = inventory.Id,
                    ProductId = inventory.ProductId,
                    MaterialId = inventory.MaterialId,
                    WarehouseLocationId = inventory.WarehouseLocationId,
                    QuantityChange = adjustment.Variance,
                    TransactionType = TransactionType.StockAdjustment,
                    ReferenceId = adjustment.Id,
                    Note = adjustment.Reason,
                    CreatedByUserId = ceoId,
                    CreatedAt = DateTime.UtcNow
                });

                adjustment.Status = StockAdjustmentStatus.Approved;
            }

            adjustment.DecidedByUserId = ceoId;
            adjustment.DecidedAt = DateTime.UtcNow;
            adjustment.DecisionNote = request.Note?.Trim();

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            try
            {
                var resultText = adjustment.Status == StockAdjustmentStatus.Approved ? "được duyệt" : "bị từ chối";
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_35_StockAdjustmentDecisionResult,
                    adjustment.ProposedByUserId,
                    "Kết quả đề xuất điều chỉnh tồn kho",
                    $"Đề xuất điều chỉnh tồn kho của bạn đã {resultText}.",
                    adjustment.Id,
                    "StockAdjustment"
                );
            }
            catch (Exception ex)
            {
                // Quyết định đã commit thành công -> lỗi gửi thông báo không được làm fail request, chỉ log.
                Console.WriteLine($"[StockAdjustmentService] Error sending decision-result notification: {ex.Message}");
            }

            return await GetByIdAsync(adjustment.Id);
        }
    }
}
