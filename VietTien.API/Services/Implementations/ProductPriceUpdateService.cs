using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.ProductPriceUpdate;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class ProductPriceUpdateService : IProductPriceUpdateService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IAuditLogService _auditLogService;

        public ProductPriceUpdateService(
            IUnitOfWork unitOfWork,
            ApplicationDbContext context,
            INotificationService notificationService,
            IAuditLogService auditLogService)
        {
            _unitOfWork = unitOfWork;
            _context = context;
            _notificationService = notificationService;
            _auditLogService = auditLogService;
        }

        private static DateTime VietnamToday => DateTime.UtcNow.AddHours(7).Date;

        private IQueryable<ProductPriceUpdateOrder> OrdersWithDetails() =>
            _context.ProductPriceUpdateOrders
                .Include(o => o.ProposedByUser)
                .Include(o => o.AssignedByManager)
                .Include(o => o.AssignedSalesStaff)
                .Include(o => o.ExecutedByUser)
                .Include(o => o.Items).ThenInclude(i => i.Product);

        public async Task<ProductPriceUpdateOrderDto> ProposeAsync(Guid ceoUserId, CreateProductPriceUpdateOrderRequest request)
        {
            var productIds = request.Items.Select(i => i.ProductId).ToList();
            if (productIds.Distinct().Count() != productIds.Count)
                throw new Exception("Danh sách sản phẩm bị trùng — mỗi sản phẩm chỉ được xuất hiện 1 lần trong 1 đợt.");

            // Chặn 2 đợt cùng "mở" (chưa Executed/Cancelled) đè giá lên cùng 1 sản phẩm — tránh OldPrice
            // của đợt sau bị lệch so với giá thật khi đợt trước thực thi trước.
            var openProductIds = await _context.ProductPriceUpdateOrderItems
                .Where(i => productIds.Contains(i.ProductId)
                    && (i.ProductPriceUpdateOrder.Status == ProductPriceUpdateOrderStatus.Proposed
                        || i.ProductPriceUpdateOrder.Status == ProductPriceUpdateOrderStatus.Notified))
                .Select(i => i.ProductId)
                .Distinct()
                .ToListAsync();
            if (openProductIds.Count > 0)
            {
                var names = await _context.Products.Where(p => openProductIds.Contains(p.Id)).Select(p => p.Name).ToListAsync();
                throw new Exception($"Sản phẩm đang có đợt cập nhật giá khác chưa xử lý xong: {string.Join(", ", names)}.");
            }

            var order = new ProductPriceUpdateOrder
            {
                Status = ProductPriceUpdateOrderStatus.Proposed,
                ProposedByUserId = ceoUserId,
                ProposedAt = DateTime.UtcNow,
                ProposalNote = request.ProposalNote,
                ScheduledEffectiveDate = request.ScheduledEffectiveDate.Date
            };

            foreach (var reqItem in request.Items)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == reqItem.ProductId);
                if (product == null || product.IsDiscontinued)
                    throw new Exception($"Sản phẩm {reqItem.ProductId} không tồn tại hoặc đã ngừng kinh doanh.");
                if (reqItem.NewPrice == product.StandardListedPrice)
                    throw new Exception($"Giá mới của sản phẩm {product.Name} trùng với giá hiện tại — không cần tạo đợt cập nhật.");

                order.Items.Add(new ProductPriceUpdateOrderItem
                {
                    ProductId = product.Id,
                    OldPrice = product.StandardListedPrice,
                    NewPrice = reqItem.NewPrice
                });
            }

            _context.ProductPriceUpdateOrders.Add(order);
            await _unitOfWork.SaveChangesAsync();

            // Đợt cập nhật giá đã được lưu thành công ở trên -> lỗi gửi notification không được làm
            // fail request đề xuất, chỉ log để theo dõi.
            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_44_ProductPriceUpdateOrderProposed,
                    SystemRole.SalesManager,
                    "Đề xuất cập nhật giá cần phân công",
                    $"CEO vừa đề xuất cập nhật giá cho {order.Items.Count} sản phẩm, hiệu lực từ {order.ScheduledEffectiveDate:dd/MM/yyyy}. Vui lòng phân công nhân viên Sale phụ trách và thông báo cho khách hàng.",
                    order.Id,
                    "ProductPriceUpdateOrder"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProductPriceUpdateService] Error sending proposed notification: {ex.Message}");
            }

            return await GetByIdAsync(order.Id);
        }

        public async Task<ProductPriceUpdateOrderDto> AssignAndNotifyAsync(Guid orderId, Guid managerId, AssignPriceUpdateOrderRequest request)
        {
            var order = await OrdersWithDetails().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Đợt cập nhật giá không tồn tại.");
            if (order.Status != ProductPriceUpdateOrderStatus.Proposed)
                throw new InvalidOperationException("Đợt cập nhật giá này đã được xử lý, không thể phân công lại.");

            var staff = await _unitOfWork.Users.GetByIdAsync(request.StaffId);
            if (staff == null || staff.Role != SystemRole.SalesStaff)
                throw new Exception("Nhân viên được chọn không hợp lệ.");
            if (!staff.IsActive)
                throw new Exception("Nhân viên được chọn hiện đang bị khóa tài khoản.");

            order.AssignedSalesStaffId = request.StaffId;
            order.AssignedByManagerId = managerId;
            order.Status = ProductPriceUpdateOrderStatus.Notified;
            order.NotifiedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            var productIds = order.Items.Select(i => i.ProductId).ToList();
            var productNames = string.Join(", ", order.Items.Select(i => i.Product.Name));

            try
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_45_ProductPriceUpdateOrderAssigned,
                    request.StaffId,
                    "Được phân công thực hiện cập nhật giá",
                    $"Sales Manager vừa phân công bạn thực hiện cập nhật giá cho {order.Items.Count} sản phẩm ({productNames}) vào ngày {order.ScheduledEffectiveDate:dd/MM/yyyy}.",
                    order.Id,
                    "ProductPriceUpdateOrder"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProductPriceUpdateService] Error sending assignment notification: {ex.Message}");
            }

            // Khách hàng bị ảnh hưởng: đang có sản phẩm trong giỏ, HOẶC có báo giá CustomerAccepted còn
            // hạn chứa sản phẩm đó (giá đàm phán của họ cũng sẽ dịch chuyển theo — xem EffectiveNegotiatedUnitPrice
            // ở OrderService.CalculateDiscountAsync). Mirror đúng điều kiện lọc đã dùng ở đó.
            var cartCustomerIds = _context.CartItems
                .Where(ci => productIds.Contains(ci.ProductId))
                .Select(ci => ci.Cart.CustomerProfileId);

            var quotationCustomerIds = _context.QuotationVersionItems
                .Where(i => productIds.Contains(i.ProductId)
                    && i.QuotationVersion.Quotation.Status == QuotationStatus.CustomerAccepted
                    && i.QuotationVersionId == i.QuotationVersion.Quotation.AcceptedVersionId
                    && (i.QuotationVersion.Quotation.ValidUntil == null || i.QuotationVersion.Quotation.ValidUntil >= DateTime.UtcNow))
                .Select(i => i.QuotationVersion.Quotation.CustomerProfileId);

            var affectedProfileIds = await cartCustomerIds.Union(quotationCustomerIds).Distinct().ToListAsync();
            if (affectedProfileIds.Count > 0)
            {
                var affectedUserIds = await _context.CustomerProfiles
                    .Where(cp => affectedProfileIds.Contains(cp.Id))
                    .Select(cp => cp.UserId)
                    .Distinct()
                    .ToListAsync();

                foreach (var userId in affectedUserIds)
                {
                    try
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_46_ProductPriceUpdateScheduleNotice,
                            userId,
                            "Thông báo lịch cập nhật giá sản phẩm",
                            $"Một số sản phẩm trong giỏ hàng/báo giá của bạn ({productNames}) sẽ được cập nhật giá vào ngày {order.ScheduledEffectiveDate:dd/MM/yyyy}. Nếu sản phẩm đang trong giỏ hàng, giá hiện tại của bạn sẽ được giữ nguyên trong 24h kể từ lúc áp dụng giá mới.",
                            order.Id,
                            "ProductPriceUpdateOrder"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProductPriceUpdateService] Error notifying affected customer {userId}: {ex.Message}");
                    }
                }
            }

            return await GetByIdAsync(order.Id);
        }

        public async Task<ProductPriceUpdateOrderDto> ExecuteAsync(Guid orderId, Guid staffId)
        {
            var order = await OrdersWithDetails().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Đợt cập nhật giá không tồn tại.");
            if (order.Status != ProductPriceUpdateOrderStatus.Notified)
                throw new InvalidOperationException("Đợt cập nhật giá này chưa được thông báo hoặc đã xử lý xong.");
            if (order.AssignedSalesStaffId != staffId)
                throw new UnauthorizedAccessException("Bạn không được phân công thực hiện đợt cập nhật giá này.");
            if (VietnamToday < order.ScheduledEffectiveDate.Date)
                throw new InvalidOperationException($"Chưa đến ngày áp dụng ({order.ScheduledEffectiveDate:dd/MM/yyyy}).");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var productIds = order.Items.Select(i => i.ProductId).ToList();

                foreach (var item in order.Items)
                {
                    if (item.Product.StandardListedPrice != item.OldPrice)
                        throw new InvalidOperationException(
                            $"Giá sản phẩm {item.Product.Name} đã bị thay đổi bởi tác vụ khác kể từ lúc đề xuất. Vui lòng huỷ đợt này và tạo đề xuất mới.");

                    var before = new { item.Product.StandardListedPrice };
                    item.Product.StandardListedPrice = item.NewPrice;

                    await _auditLogService.LogAsync(
                        "Product", item.ProductId.ToString(), "PriceUpdate",
                        staffId, null, "SalesStaff",
                        before, new { StandardListedPrice = item.NewPrice },
                        reason: $"ProductPriceUpdateOrder {order.Id}");
                }

                // Khoá giá 24h cho mọi dòng giỏ hàng đang chứa các sản phẩm này — UnitPrice giữ nguyên
                // (không đổi), chỉ đánh dấu mốc để CartService/OrderService tính cửa sổ giữ giá riêng.
                var affectedCartItems = await _context.CartItems.Where(ci => productIds.Contains(ci.ProductId)).ToListAsync();
                var executedAt = DateTime.UtcNow;
                foreach (var ci in affectedCartItems) ci.PriceLockedAt = executedAt;

                order.Status = ProductPriceUpdateOrderStatus.Executed;
                order.ExecutedAt = executedAt;
                order.ExecutedByUserId = staffId;

                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            try
            {
                var note = $"Sales Staff vừa thực hiện cập nhật giá cho {order.Items.Count} sản phẩm (đợt đề xuất ngày {order.ProposedAt:dd/MM/yyyy}, hiệu lực {order.ScheduledEffectiveDate:dd/MM/yyyy}).";
                await _notificationService.CreateRoleNotificationAsync(NotificationType.SYS_47_ProductPriceUpdateOrderExecuted, SystemRole.CEO, "Đợt cập nhật giá đã hoàn tất", note, order.Id, "ProductPriceUpdateOrder");
                if (order.AssignedByManagerId.HasValue)
                    await _notificationService.CreateNotificationAsync(NotificationType.SYS_47_ProductPriceUpdateOrderExecuted, order.AssignedByManagerId.Value, "Đợt cập nhật giá đã hoàn tất", note, order.Id, "ProductPriceUpdateOrder");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProductPriceUpdateService] Error sending executed notification: {ex.Message}");
            }

            return await GetByIdAsync(order.Id);
        }

        public async Task<ProductPriceUpdateOrderDto> CancelAsync(Guid orderId, Guid actorUserId, string actorRole, CancelPriceUpdateOrderRequest request)
        {
            var order = await OrdersWithDetails().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Đợt cập nhật giá không tồn tại.");
            if (order.Status == ProductPriceUpdateOrderStatus.Executed)
                throw new InvalidOperationException("Đợt cập nhật giá đã thực hiện, không thể huỷ.");
            if (order.Status == ProductPriceUpdateOrderStatus.Cancelled)
                throw new InvalidOperationException("Đợt cập nhật giá này đã bị huỷ trước đó.");
            if (actorRole == "SalesManager" && order.Status != ProductPriceUpdateOrderStatus.Proposed)
                throw new InvalidOperationException("Sales Manager chỉ huỷ được khi chưa gửi thông báo cho khách hàng — vui lòng nhờ CEO huỷ.");

            var wasNotified = order.Status == ProductPriceUpdateOrderStatus.Notified;

            order.Status = ProductPriceUpdateOrderStatus.Cancelled;
            order.CancelledByUserId = actorUserId;
            order.CancelledAt = DateTime.UtcNow;
            order.CancelReason = request.Reason;

            await _unitOfWork.SaveChangesAsync();

            if (wasNotified && order.AssignedSalesStaffId.HasValue)
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_48_ProductPriceUpdateOrderCancelled,
                        order.AssignedSalesStaffId.Value,
                        "Đợt cập nhật giá đã bị huỷ",
                        $"Đợt cập nhật giá bạn được phân công (hiệu lực {order.ScheduledEffectiveDate:dd/MM/yyyy}) đã bị huỷ." + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $" Lý do: {request.Reason}"),
                        order.Id,
                        "ProductPriceUpdateOrder"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ProductPriceUpdateService] Error sending cancel notification: {ex.Message}");
                }
            }

            return await GetByIdAsync(order.Id);
        }

        public async Task<ProductPriceUpdateOrderDto> GetByIdAsync(Guid orderId)
        {
            var order = await OrdersWithDetails().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Đợt cập nhật giá không tồn tại.");
            return MapToDto(order);
        }

        public async Task<IEnumerable<ProductPriceUpdateOrderDto>> GetAllAsync()
        {
            var list = await OrdersWithDetails().OrderByDescending(o => o.ProposedAt).ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<ProductPriceUpdateOrderDto>> GetPendingForManagerAsync()
        {
            var list = await OrdersWithDetails()
                .Where(o => o.Status == ProductPriceUpdateOrderStatus.Proposed)
                .OrderByDescending(o => o.ProposedAt)
                .ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<ProductPriceUpdateOrderDto>> GetPendingForStaffAsync(Guid staffId)
        {
            var list = await OrdersWithDetails()
                .Where(o => o.Status == ProductPriceUpdateOrderStatus.Notified && o.AssignedSalesStaffId == staffId)
                .OrderBy(o => o.ScheduledEffectiveDate)
                .ToListAsync();
            return list.Select(MapToDto);
        }

        private string ComputeComplianceStatus(ProductPriceUpdateOrder o)
        {
            if (o.Status == ProductPriceUpdateOrderStatus.Cancelled) return "Cancelled";
            if (o.Status == ProductPriceUpdateOrderStatus.Proposed) return "AwaitingAssignment";
            if (o.Status == ProductPriceUpdateOrderStatus.Executed)
                return o.ExecutedAt!.Value.AddHours(7).Date <= o.ScheduledEffectiveDate.Date ? "OnTime" : "Late";
            // Notified, chưa Executed
            return VietnamToday > o.ScheduledEffectiveDate.Date ? "Overdue" : "PendingExecution";
        }

        private ProductPriceUpdateOrderDto MapToDto(ProductPriceUpdateOrder o) => new ProductPriceUpdateOrderDto
        {
            Id = o.Id,
            Status = o.Status.ToString(),
            ComplianceStatus = ComputeComplianceStatus(o),
            ProposedByUserId = o.ProposedByUserId,
            ProposedByName = o.ProposedByUser?.FullName ?? "Unknown",
            ProposedAt = o.ProposedAt,
            ProposalNote = o.ProposalNote,
            ScheduledEffectiveDate = o.ScheduledEffectiveDate,
            AssignedByManagerId = o.AssignedByManagerId,
            AssignedByManagerName = o.AssignedByManager?.FullName,
            AssignedSalesStaffId = o.AssignedSalesStaffId,
            AssignedSalesStaffName = o.AssignedSalesStaff?.FullName,
            NotifiedAt = o.NotifiedAt,
            ExecutedByUserId = o.ExecutedByUserId,
            ExecutedByName = o.ExecutedByUser?.FullName,
            ExecutedAt = o.ExecutedAt,
            CancelledByUserId = o.CancelledByUserId,
            CancelledAt = o.CancelledAt,
            CancelReason = o.CancelReason,
            Items = o.Items.Select(i => new ProductPriceUpdateOrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? "Unknown",
                ProductImageUrl = i.Product?.ImageUrl,
                OldPrice = i.OldPrice,
                NewPrice = i.NewPrice
            }).ToList()
        };
    }
}
