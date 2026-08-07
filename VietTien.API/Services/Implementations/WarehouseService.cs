using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class WarehouseService : IWarehouseService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<SalesHub> _salesHub;
        private readonly INotificationService _notificationService;
        private readonly ILogger<WarehouseService> _logger;

        public WarehouseService(ApplicationDbContext context, IHubContext<SalesHub> salesHub, INotificationService notificationService, ILogger<WarehouseService> logger)
        {
            _context = context;
            _salesHub = salesHub;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<List<WarehouseOrderListDto>> GetOrdersForWarehouseAsync(string tabType, int pageNumber, int pageSize)
        {
            var query = _context.Orders
                .Include(o => o.OrderItems)
                .AsQueryable();

            switch (tabType)
            {
                case "OnlinePending":
                    query = query.Where(o => o.OrderStatus == OrderStatus.Confirmed && o.FulfillmentStatus <= FulfillmentStatus.Allocated && !o.IsExternalOrder);
                    break;
                case "ExternalPending":
                    query = query.Where(o => o.OrderStatus == OrderStatus.Confirmed && o.FulfillmentStatus <= FulfillmentStatus.Allocated && o.IsExternalOrder);
                    break;
                case "InProgress":
                    query = query.Where(o => o.FulfillmentStatus == FulfillmentStatus.Picking);
                    break;
                case "Consolidation":
                    query = query.Where(o => o.FulfillmentStatus == FulfillmentStatus.Ready || o.FulfillmentStatus == FulfillmentStatus.Consolidating);
                    break;
                case "Handover":
                    query = query.Where(o => o.FulfillmentStatus == FulfillmentStatus.Consolidated);
                    break;
                case "GoodsIssue":
                    query = query.Where(o => o.FulfillmentStatus == FulfillmentStatus.HandedOver);
                    break;
                default:
                    throw new ArgumentException("Tab Type không hợp lệ.");
            }

            // Sắp xếp FIFO: Ưu tiên đơn tạo trước
            query = query.OrderBy(o => o.CreatedAt);

            var orders = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var orderIds = orders.Select(o => o.Id).ToList();
            var handovers = tabType == "Handover" 
                ? await _context.HandoverRecords.Where(h => orderIds.Contains(h.OrderId)).ToListAsync()
                : new List<HandoverRecord>();

            var orderDtos = new List<WarehouseOrderListDto>();
            foreach (var o in orders)
            {
                var handover = handovers.FirstOrDefault(h => h.OrderId == o.Id);
                var dto = new WarehouseOrderListDto
                {
                    OrderId = o.Id,
                    OrderCode = o.OrderCode,
                    ConfirmedAt = o.CreatedAt,
                    TotalQuantity = o.OrderItems.Sum(i => i.Quantity),
                    Status = o.FulfillmentStatus.ToString(),
                    OrderProgress = o.OrderItems.Sum(i => i.Quantity) > 0 ? (o.OrderItems.Sum(i => i.PackedQuantity) * 100 / o.OrderItems.Sum(i => i.Quantity)) : 0,
                    PickingStartedAt = o.PickingStartedAt,
                    PickingCompletedAt = o.PickingCompletedAt,
                    AllocatedWarehouse = "Kho mặc định",
                    AllocatedWarehouseCode = "WH-DEFAULT",
                    WarehouseConfirmed = handover != null && !string.IsNullOrEmpty(handover.WarehouseSignature),
                    SalesConfirmed = handover != null && !string.IsNullOrEmpty(handover.SalesSignature)
                };

                var requiresTransfer = false;
                foreach (var item in o.OrderItems)
                {
                    var defaultWarehouseStock = await _context.Inventories
                        .Where(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Code == "WH-DEFAULT")
                        .SumAsync(inv => inv.OnHandQuantity);
                        
                    if (item.Quantity > defaultWarehouseStock)
                    {
                        requiresTransfer = true;
                        break;
                    }
                }
                dto.RequiresTransfer = requiresTransfer;

                var firstItem = o.OrderItems.FirstOrDefault();
                if (firstItem != null)
                {
                    var inventoriesForProduct = await _context.Inventories
                        .Include(i => i.WarehouseLocation)
                        .ThenInclude(wl => wl.Warehouse)
                        .Where(i => i.ProductId == firstItem.ProductId && i.WarehouseLocation != null && i.WarehouseLocation.Warehouse != null)
                        .ToListAsync();
                        
                    var inv = inventoriesForProduct.FirstOrDefault(i => i.WarehouseLocation!.Warehouse!.Code == "WH-DEFAULT" && i.OnHandQuantity > 0)
                              ?? inventoriesForProduct.FirstOrDefault(i => i.OnHandQuantity > 0)
                              ?? inventoriesForProduct.FirstOrDefault();
                    
                    if (inv != null && !string.IsNullOrEmpty(inv.WarehouseLocation?.Warehouse?.Name))
                    {
                        dto.AllocatedWarehouse = inv.WarehouseLocation.Warehouse.Name;
                        dto.AllocatedWarehouseCode = "WH-DEFAULT"; // Force final consolidation destination
                    }
                }
                orderDtos.Add(dto);
            }

            return orderDtos;
        }

        public async Task<WarehouseOrderDetailDto> GetOrderDetailAsync(Guid orderId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            var pickTasks = await _context.PickTasks
                .Include(pt => pt.Warehouse)
                .Include(pt => pt.Items)
                .ThenInclude(pti => pti.Product)
                .Where(pt => pt.OrderId == orderId)
                .ToListAsync();

            int totalRequested = order.OrderItems.Sum(i => i.Quantity);
            // Re-calculate packed based on PickTasks, or just fallback to order
            int totalPacked = pickTasks.Any() ? pickTasks.SelectMany(pt => pt.Items).Sum(i => i.PickedQuantity) : order.OrderItems.Sum(i => i.PackedQuantity);
            int progress = totalRequested > 0 ? (totalPacked * 100 / totalRequested) : 0;

            var detailDto = new WarehouseOrderDetailDto
            {
                OrderId = order.Id,
                OrderCode = order.OrderCode,
                Status = order.OrderStatus.ToString(),
                CreatedAt = order.CreatedAt,
                AllocatedWarehouse = "Kho mặc định",
                AllocatedWarehouseCode = "WH-DEFAULT",
                OrderProgress = progress,
                PickingStartedAt = order.PickingStartedAt,
                PickingCompletedAt = order.PickingCompletedAt
            };

            foreach (var item in order.OrderItems)
            {
                var inventories = await _context.Inventories
                    .Include(i => i.WarehouseLocation)
                    .ThenInclude(wl => wl.Warehouse)
                    .Where(inv => inv.ProductId == item.ProductId)
                    .ToListAsync();
                    
                var physicalStock = inventories.Sum(inv => inv.OnHandQuantity);
                
                var defaultWarehouseStock = inventories
                    .Where(inv => inv.WarehouseLocation?.Warehouse?.Code == "WH-DEFAULT")
                    .Sum(inv => inv.OnHandQuantity);
                
                var requiredTransfer = Math.Max(0, item.Quantity - defaultWarehouseStock);

                if (detailDto.AllocatedWarehouse == "Kho mặc định" && inventories.Any(inv => inv.WarehouseLocation?.Warehouse?.Name != null))
                {
                    var firstInv = inventories.FirstOrDefault(inv => inv.WarehouseLocation?.Warehouse?.Code == "WH-DEFAULT" && inv.OnHandQuantity > 0)
                                   ?? inventories.FirstOrDefault(inv => inv.OnHandQuantity > 0)
                                   ?? inventories.FirstOrDefault(inv => inv.WarehouseLocation?.Warehouse?.Name != null);
                    detailDto.AllocatedWarehouse = firstInv?.WarehouseLocation.Warehouse.Name ?? "Kho mặc định";
                    detailDto.AllocatedWarehouseCode = "WH-DEFAULT"; // Destination is always WH-DEFAULT
                }

                detailDto.Items.Add(new WarehouseOrderItemDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    SKU = item.Product.Sku,
                    RequestedQuantity = item.Quantity,
                    PhysicalStock = physicalStock,
                    IsStockSufficient = physicalStock >= item.Quantity,
                    PackedQuantity = item.PackedQuantity,
                    RemainingQuantity = item.Quantity - item.PackedQuantity,
                    EvidenceImageUrl = item.EvidenceImageUrl,
                    RequiredTransferQuantity = requiredTransfer
                });
            }

            foreach (var pt in pickTasks)
            {
                var ptDto = new PickTaskDto
                {
                    PickTaskId = pt.Id,
                    WarehouseName = pt.Warehouse?.Name ?? "Unknown",
                    WarehouseCode = pt.Warehouse?.Code ?? "UNKNOWN",
                    Status = pt.Status.ToString(),
                    Items = pt.Items.Select(pti => new WarehouseOrderItemDto
                    {
                        ProductId = pti.ProductId,
                        ProductName = pti.Product.Name,
                        SKU = pti.Product.Sku,
                        RequestedQuantity = pti.QuantityToPick,
                        PackedQuantity = pti.PickedQuantity,
                        RemainingQuantity = pti.QuantityToPick - pti.PickedQuantity
                    }).ToList()
                };
                detailDto.PickTasks.Add(ptDto);
            }

            return detailDto;
        }

        public async Task AcceptOrderAsync(Guid orderId, Guid staffId)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.FulfillmentStatus != FulfillmentStatus.Allocated && order.FulfillmentStatus != FulfillmentStatus.Unallocated && order.FulfillmentStatus != FulfillmentStatus.Picking)
                throw new Exception("Đơn hàng chưa được phân bổ hoặc không ở trạng thái hợp lệ, không thể xử lý.");

            order.FulfillmentStatus = FulfillmentStatus.Picking;
            order.WarehouseStaffId = staffId;
            order.PickingStartedAt = DateTime.UtcNow;

            // Generate PickTasks if they don't exist (for older orders before the new flow)
            var existingTasks = await _context.PickTasks.AnyAsync(pt => pt.OrderId == orderId);
            if (!existingTasks)
            {
                var defaultWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Code == "WH-DEFAULT");
                if (defaultWarehouse == null) throw new KeyNotFoundException("Không tìm thấy kho mặc định (WH-DEFAULT).");

                var warehouseTasks = new Dictionary<Guid, PickTask>();

                PickTask GetOrCreateTask(Guid warehouseId)
                {
                    if (!warehouseTasks.TryGetValue(warehouseId, out var task))
                    {
                        task = new PickTask
                        {
                            Id = Guid.NewGuid(),
                            OrderId = order.Id,
                            WarehouseId = warehouseId,
                            Status = PickTaskStatus.Pending,
                            CreatedAt = DateTime.UtcNow
                        };
                        warehouseTasks[warehouseId] = task;
                        _context.PickTasks.Add(task);
                    }
                    return task;
                }

                foreach (var item in order.OrderItems)
                {
                    var quantityRemaining = item.Quantity;

                    var defaultInv = await _context.Inventories
                        .Include(inv => inv.WarehouseLocation)
                        .ThenInclude(wl => wl.Warehouse)
                        .FirstOrDefaultAsync(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id == defaultWarehouse.Id);

                    if (defaultInv != null && defaultInv.OnHandQuantity > 0)
                    {
                        var takeQty = Math.Min(quantityRemaining, defaultInv.OnHandQuantity);
                        var task = GetOrCreateTask(defaultWarehouse.Id);
                        task.Items.Add(new PickTaskItem
                        {
                            PickTaskId = task.Id,
                            ProductId = item.ProductId,
                            QuantityToPick = takeQty,
                            PickedQuantity = 0
                        });
                        quantityRemaining -= takeQty;
                    }

                    if (quantityRemaining > 0)
                    {
                        var otherInvs = await _context.Inventories
                            .Include(inv => inv.WarehouseLocation)
                            .ThenInclude(wl => wl.Warehouse)
                            .Where(inv => inv.ProductId == item.ProductId && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Id != defaultWarehouse.Id && inv.OnHandQuantity > 0)
                            .OrderByDescending(inv => inv.OnHandQuantity)
                            .ToListAsync();

                        foreach (var inv in otherInvs)
                        {
                            if (quantityRemaining <= 0) break;
                            var takeQty = Math.Min(quantityRemaining, inv.OnHandQuantity);
                            var task = GetOrCreateTask(inv.WarehouseLocation!.WarehouseId);
                            task.Items.Add(new PickTaskItem
                            {
                                PickTaskId = task.Id,
                                ProductId = item.ProductId,
                                QuantityToPick = takeQty,
                                PickedQuantity = 0
                            });
                            quantityRemaining -= takeQty;
                        }

                        if (quantityRemaining > 0)
                        {
                            var task = GetOrCreateTask(defaultWarehouse.Id);
                            var existingItem = task.Items.FirstOrDefault(i => i.ProductId == item.ProductId);
                            if (existingItem != null) {
                                existingItem.QuantityToPick += quantityRemaining;
                            } else {
                                task.Items.Add(new PickTaskItem
                                {
                                    PickTaskId = task.Id,
                                    ProductId = item.ProductId,
                                    QuantityToPick = quantityRemaining,
                                    PickedQuantity = 0
                                });
                            }
                        }
                    }
                }
            }

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        public async Task<List<PickTaskDto>> GetPickTasksAsync(string tabType, int pageNumber, int pageSize)
        {
            var query = _context.PickTasks
                .Include(pt => pt.Order)
                .Include(pt => pt.Warehouse)
                .Include(pt => pt.Items)
                .ThenInclude(pti => pti.Product)
                .AsQueryable();

            if (tabType == "New")
            {
                query = query.Where(pt => pt.Status == PickTaskStatus.Pending);
            }
            else if (tabType == "InProgress")
            {
                query = query.Where(pt => pt.Status == PickTaskStatus.Picking || pt.Status == PickTaskStatus.Exception);
            }
            else if (tabType == "History")
            {
                query = query.Where(pt => pt.Status == PickTaskStatus.Completed);
            }

            query = query.OrderBy(pt => pt.CreatedAt);

            var pickTasks = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var dtos = new List<PickTaskDto>();
            foreach (var pt in pickTasks)
            {
                dtos.Add(new PickTaskDto
                {
                    PickTaskId = pt.Id,
                    OrderId = pt.OrderId,
                    OrderCode = pt.Order?.OrderCode ?? string.Empty,
                    WarehouseName = pt.Warehouse?.Name ?? "Unknown",
                    WarehouseCode = pt.Warehouse?.Code ?? "UNKNOWN",
                    Status = pt.Status.ToString(),
                    Items = pt.Items.Select(pti => new WarehouseOrderItemDto
                    {
                        ProductId = pti.ProductId,
                        ProductName = pti.Product.Name,
                        SKU = pti.Product.Sku,
                        RequestedQuantity = pti.QuantityToPick,
                        PackedQuantity = pti.PickedQuantity,
                        RemainingQuantity = pti.QuantityToPick - pti.PickedQuantity
                    }).ToList()
                });
            }

            return dtos;
        }

        public async Task<PickTaskDto> GetPickTaskDetailAsync(Guid pickTaskId)
        {
            var pt = await _context.PickTasks
                .Include(p => p.Warehouse)
                .Include(p => p.Items)
                .ThenInclude(pti => pti.Product)
                .FirstOrDefaultAsync(p => p.Id == pickTaskId);

            if (pt == null) throw new KeyNotFoundException("Không tìm thấy lệnh xuất kho.");

            return new PickTaskDto
            {
                PickTaskId = pt.Id,
                OrderId = pt.OrderId,
                OrderCode = pt.Order?.OrderCode ?? string.Empty,
                WarehouseName = pt.Warehouse?.Name ?? "Unknown",
                WarehouseCode = pt.Warehouse?.Code ?? "UNKNOWN",
                Status = pt.Status.ToString(),
                Items = pt.Items.Select(pti => new WarehouseOrderItemDto
                {
                    ProductId = pti.ProductId,
                    ProductName = pti.Product.Name,
                    SKU = pti.Product.Sku,
                    RequestedQuantity = pti.QuantityToPick,
                    PackedQuantity = pti.PickedQuantity,
                    RemainingQuantity = pti.QuantityToPick - pti.PickedQuantity
                }).ToList()
            };
        }

        public async Task AcceptPickTaskAsync(Guid pickTaskId, Guid staffId)
        {
            var task = await _context.PickTasks
                .Include(pt => pt.Order)
                .FirstOrDefaultAsync(p => p.Id == pickTaskId);
            
            if (task == null) throw new KeyNotFoundException("Không tìm thấy lệnh xuất kho.");

            if (task.Status != PickTaskStatus.Pending)
                throw new Exception("Lệnh xuất kho không ở trạng thái chờ tiếp nhận.");

            if (task.AssignedUserId != null && task.AssignedUserId != staffId)
                throw new Exception("Lệnh xuất kho này đã được nhân viên khác tiếp nhận.");

            task.Status = PickTaskStatus.Picking;
            task.AssignedUserId = staffId;
            
            // Also update order status if it's the first task being picked
            if (task.Order != null && task.Order.OrderStatus == OrderStatus.Confirmed)
            {
                task.Order.OrderStatus = OrderStatus.Processing;
                task.Order.FulfillmentStatus = FulfillmentStatus.Picking;
                task.Order.PickingStartedAt = DateTime.UtcNow;
            }

            _context.PickTasks.Update(task);
            if (task.Order != null) _context.Orders.Update(task.Order);
            
            await _context.SaveChangesAsync();
        }

        public async Task ReportShortageAsync(Guid orderId, Guid staffId, ShortageAlertRequestDto alert)
        {
            var order = await _context.Orders.Include(o => o.OrderItems).ThenInclude(i => i.Product).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.WarehouseStaffId != staffId)
                throw new UnauthorizedAccessException("Bạn không có quyền báo cáo cho đơn hàng này.");

            order.OrderStatus = OrderStatus.PendingConfirmation; // Revert to confirmation phase
            order.FulfillmentStatus = FulfillmentStatus.Unallocated;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            var product = order.OrderItems.FirstOrDefault(i => i.ProductId == alert.ProductId)?.Product;
            var productName = product != null ? product.Name : "Không xác định";
            var message = $"Đơn hàng {order.OrderCode} thiếu {alert.MissingQuantity} {productName}. Ghi chú: {alert.Note}";

            // Đơn đã revert trạng thái và commit thành công ở trên -> lỗi báo SignalR/Notification
            // không được làm request báo lỗi cho warehouse staff (client đã thực sự báo thiếu hàng
            // thành công), chỉ log để theo dõi.
            try
            {
                // Bắn SignalR alert về cho Sales (cũ)
                await _salesHub.Clients.Group("SalesStaff").SendAsync("ReceiveShortageAlert", new
                {
                    OrderId = orderId,
                    OrderCode = order.OrderCode,
                    Message = message
                });

                // Gửi Notification SYS-07
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_07_WarehouseShortage,
                    SystemRole.SalesManager,
                    "Kho báo thiếu hàng",
                    message,
                    order.Id,
                    "Order"
                );

                if (order.CustomerProfile?.AssignedSalesStaffId != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_07_WarehouseShortage,
                        order.CustomerProfile.AssignedSalesStaffId.Value,
                        "Kho báo thiếu hàng",
                        message,
                        order.Id,
                        "Order"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi cảnh báo thiếu hàng cho đơn {OrderId}", order.Id);
            }
        }

        public async Task UpdatePickTaskItemProgressAsync(Guid pickTaskId, Guid staffId, Guid productId, int pickedQty, string? imageUrl)
        {
            var task = await _context.PickTasks
                .Include(pt => pt.Items)
                .Include(pt => pt.Order)
                .ThenInclude(o => o!.OrderItems)
                .FirstOrDefaultAsync(p => p.Id == pickTaskId);
                
            if (task == null) throw new KeyNotFoundException("Không tìm thấy lệnh xuất kho.");

            if (task.Status != PickTaskStatus.Picking && task.Status != PickTaskStatus.Exception)
                throw new Exception("Lệnh xuất kho chưa được bắt đầu chuẩn bị (picking).");

            var item = task.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item == null) throw new KeyNotFoundException("Không tìm thấy sản phẩm trong lệnh xuất kho.");

            if (pickedQty > item.QuantityToPick)
                throw new Exception("Số lượng đóng gói không được vượt quá số lượng yêu cầu của lệnh.");

            item.PickedQuantity = pickedQty;
            
            // Note: Currently we only update PickTaskItem. We should also optionally update OrderItem if we want backward compatibility, but conceptually we'll rely on PickTasks.
            if (task.Order != null)
            {
                var orderItem = task.Order.OrderItems.FirstOrDefault(oi => oi.ProductId == productId);
                if (orderItem != null)
                {
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        orderItem.EvidenceImageUrl = imageUrl;
                    }
                    
                    // We can aggregate packed quantity from all pick tasks later.
                    // For now, let's keep PickTaskItem the source of truth for packed items.
                }
            }

            _context.PickTasks.Update(task);
            await _context.SaveChangesAsync();
        }

        public async Task CompletePickTaskAsync(Guid pickTaskId, Guid staffId)
        {
            var task = await _context.PickTasks
                .Include(pt => pt.Items)
                .Include(pt => pt.Order)
                .FirstOrDefaultAsync(p => p.Id == pickTaskId);
                
            if (task == null) throw new KeyNotFoundException("Không tìm thấy lệnh xuất kho.");

            if (task.AssignedUserId != null && task.AssignedUserId != staffId)
                throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên lệnh này.");

            if (task.Status != PickTaskStatus.Picking && task.Status != PickTaskStatus.Exception)
                throw new Exception("Lệnh xuất kho chưa được bắt đầu chuẩn bị (picking).");

            if (task.Items.Any(i => i.PickedQuantity < i.QuantityToPick))
                throw new Exception("Chưa đóng gói đủ số lượng yêu cầu cho tất cả các sản phẩm trong lệnh.");

            task.Status = PickTaskStatus.Completed;
            
            _context.PickTasks.Update(task);
            await _context.SaveChangesAsync();

            // Check if ALL pick tasks for the order are completed
            if (task.Order != null)
            {
                var allTasks = await _context.PickTasks.Where(pt => pt.OrderId == task.OrderId).ToListAsync();
                if (allTasks.All(pt => pt.Status == PickTaskStatus.Completed))
                {
                    task.Order.FulfillmentStatus = FulfillmentStatus.Ready;
                    task.Order.PickingCompletedAt = DateTime.UtcNow;
                    _context.Orders.Update(task.Order);
                    await _context.SaveChangesAsync();

                    // Notify Sales
                    var orderWithProfile = await _context.Orders
                        .Include(o => o.CustomerProfile)
                        .FirstOrDefaultAsync(o => o.Id == task.OrderId);
                    
                    if (orderWithProfile?.CustomerProfile?.AssignedSalesStaffId != null)
                    {
                        // Đơn đã chuyển FulfillmentStatus=Ready và commit thành công ở trên -> lỗi
                        // gửi notification không được làm fail request, chỉ log để theo dõi.
                        try
                        {
                            var isMultiWarehouse = allTasks.Select(t => t.WarehouseId).Distinct().Count() > 1;
                            await _notificationService.CreateNotificationAsync(
                                isMultiWarehouse ? NotificationType.SYS_09_AllWarehousesReady : NotificationType.SYS_08_OrderReady,
                                orderWithProfile.CustomerProfile.AssignedSalesStaffId.Value,
                                "Hàng đã sẵn sàng",
                                isMultiWarehouse
                                    ? $"Đơn hàng {task.Order.OrderCode} đã được tất cả các kho soạn xong và sẵn sàng để xử lý tiếp."
                                    : $"Đơn hàng {task.Order.OrderCode} đã được kho soạn xong và sẵn sàng để xử lý tiếp.",
                                task.Order.Id,
                                "Order"
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi gửi thông báo hàng sẵn sàng cho đơn {OrderId}", task.Order.Id);
                        }
                    }
                }
            }
        }

        public async Task ConsolidateOrderAsync(Guid orderId, Guid staffId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
            
            if (order.WarehouseStaffId != staffId)
                throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên đơn hàng này.");

            if (order.FulfillmentStatus != FulfillmentStatus.Ready && order.FulfillmentStatus != FulfillmentStatus.Consolidating)
                throw new Exception("Đơn hàng chưa sẵn sàng để tập kết.");

            order.FulfillmentStatus = FulfillmentStatus.Consolidated;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        public async Task HandoverOrderAsync(Guid orderId, Guid staffId, HandoverRequestDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");

            if (order.FulfillmentStatus != FulfillmentStatus.Consolidated)
                throw new Exception("Đơn hàng chưa được tập kết để bàn giao.");

            var handover = await _context.HandoverRecords.FirstOrDefaultAsync(h => h.OrderId == orderId);
            if (handover == null)
            {
                handover = new HandoverRecord
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Status = HandoverStatus.Pending
                };
                _context.HandoverRecords.Add(handover);
            }

            if (!string.IsNullOrEmpty(dto.WarehouseSignature))
            {
                handover.WarehouseSignature = dto.WarehouseSignature;
                handover.WarehouseStaffId = staffId;
            }

            if (!string.IsNullOrEmpty(dto.SalesSignature))
            {
                handover.SalesSignature = dto.SalesSignature;
                handover.SalesStaffId = staffId; // In reality this staffId could be the Sales user ID, we just assign whoever triggered this
            }

            if (!string.IsNullOrEmpty(handover.WarehouseSignature) && !string.IsNullOrEmpty(handover.SalesSignature))
            {
                handover.Status = HandoverStatus.Confirmed;
                handover.HandoverTime = DateTime.UtcNow;
                order.FulfillmentStatus = FulfillmentStatus.HandedOver;
            }

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        public async Task PostGoodsIssueAsync(Guid orderId, Guid staffId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.Id == orderId);
                
                if (order == null) throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
                
                if (order.FulfillmentStatus != FulfillmentStatus.HandedOver)
                    throw new Exception("Đơn hàng chưa được bàn giao.");

                order.FulfillmentStatus = FulfillmentStatus.Fulfilled;

                var warehouseId = await _context.Warehouses.Where(w => w.Code == "WH-DEFAULT").Select(w => w.Id).FirstOrDefaultAsync();
                if (warehouseId == Guid.Empty) throw new KeyNotFoundException("Không tìm thấy kho mặc định (WH-DEFAULT).");

                var goodsIssue = new GoodsIssue
                {
                    Id = Guid.NewGuid(),
                    Code = "GI-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    ReferenceId = order.Id,
                    IssuedByUserId = staffId,
                    WarehouseId = warehouseId,
                    IssueDate = DateTime.UtcNow,
                    Type = GoodsIssueType.SalesOrder,
                    Status = GoodsIssueStatus.Posted
                };
                
                foreach (var item in order.OrderItems)
                {
                    var inventory = await _context.Inventories
                        .Include(inv => inv.WarehouseLocation)
                        .ThenInclude(wl => wl.Warehouse)
                        .FirstOrDefaultAsync(inv => inv.ProductId == item.ProductId && inv.OnHandQuantity >= item.Quantity && inv.WarehouseLocation != null && inv.WarehouseLocation.Warehouse!.Code == "WH-DEFAULT");
                    
                    if (inventory == null)
                        throw new Exception($"Không đủ tồn kho khả dụng cho sản phẩm {item.ProductId} tại kho tập kết (WH-DEFAULT). Vui lòng kiểm tra lại điều chuyển nội bộ.");

                    inventory.OnHandQuantity -= item.Quantity;
                    _context.Inventories.Update(inventory);

                    // Log biến động tồn kho StockTransaction — thiếu bước này khiến báo cáo xuất/nhập và
                    // "hàng chậm luân chuyển" (đọc từ StockTransaction) không thấy được các đơn đã xuất thật qua luồng này.
                    _context.StockTransactions.Add(new StockTransaction
                    {
                        InventoryId = inventory.Id,
                        ProductId = item.ProductId,
                        WarehouseLocationId = inventory.WarehouseLocationId,
                        QuantityChange = -item.Quantity,
                        TransactionType = TransactionType.GoodsIssue,
                        ReferenceId = goodsIssue.Id,
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow
                    });

                    goodsIssue.Items.Add(new GoodsIssueItem { GoodsIssueId = goodsIssue.Id, ProductId = item.ProductId, Quantity = item.Quantity });
                }

                _context.GoodsIssues.Add(goodsIssue);
                _context.Orders.Update(order);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}

