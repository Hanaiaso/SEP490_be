using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class StockTransferService : IStockTransferService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly INotificationService _notificationService;
        private readonly IWarehouseAccessGuard _warehouseAccessGuard;
        private readonly IAuditLogService _auditLogService;

        public StockTransferService(ApplicationDbContext context, IEmailService emailService, ICloudinaryService cloudinaryService, INotificationService notificationService, IWarehouseAccessGuard warehouseAccessGuard, IAuditLogService auditLogService)
        {
            _context = context;
            _emailService = emailService;
            _cloudinaryService = cloudinaryService;
            _notificationService = notificationService;
            _warehouseAccessGuard = warehouseAccessGuard;
            _auditLogService = auditLogService;
        }

        public async Task<IEnumerable<StockTransferDto>> GetAllAsync(string? status = null)
        {
            var query = _context.StockTransfers
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.CreatedByUser)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Product)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Material)
                .AsQueryable();

            // P1: trước đây tham số này chưa từng được đọc -> FE gửi status gì cũng luôn trả toàn bộ danh sách.
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<StockTransferStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(st => st.Status == parsedStatus);
            }

            var transfers = await query
                .OrderByDescending(st => st.CreatedAt)
                .ToListAsync();

            return transfers.Select(MapToDto);
        }

        public async Task<StockTransferDto> GetByIdAsync(Guid id)
        {
            var transfer = await _context.StockTransfers
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.CreatedByUser)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Product)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(st => st.Id == id);

            if (transfer == null)
                throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

            var dto = MapToDto(transfer);
            return dto;
        }

        public async Task<StockTransferDto> CreateAsync(CreateStockTransferDto dto, Guid createdByUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (dto.SourceWarehouseId == dto.DestinationWarehouseId)
                    throw new Exception("Kho xuất và kho nhập không được trùng nhau.");

                var sourceWarehouse = await _context.Warehouses.FindAsync(dto.SourceWarehouseId)
                    ?? throw new KeyNotFoundException("Không tìm thấy kho xuất.");

                var destinationWarehouse = await _context.Warehouses.FindAsync(dto.DestinationWarehouseId)
                    ?? throw new KeyNotFoundException("Không tìm thấy kho nhập.");

                if (dto.ExpectedDispatchDate.HasValue && dto.ExpectedDispatchDate.Value < DateTime.UtcNow.AddMinutes(-5))
                    throw new Exception("Thời gian xuất kho dự kiến không được nhỏ hơn thời gian hiện tại.");

                if (dto.ExpectedReceiveDate.HasValue && dto.ExpectedDispatchDate.HasValue && dto.ExpectedReceiveDate.Value < dto.ExpectedDispatchDate.Value)
                    throw new Exception("Thời gian nhập kho dự kiến không được nhỏ hơn thời gian xuất kho dự kiến.");

                foreach (var i in dto.Items)
                {
                    if (i.ProductId == null && i.MaterialId == null)
                        throw new Exception("Mỗi mặt hàng điều chuyển phải chọn Thành phẩm hoặc Nguyên liệu.");
                    if (i.ProductId != null && i.MaterialId != null)
                        throw new Exception("Chỉ được chọn 1 trong 2: Thành phẩm hoặc Nguyên liệu.");
                    if (i.Quantity <= 0)
                        throw new Exception("Số lượng điều chuyển phải lớn hơn 0.");
                }

                var transfer = new StockTransfer
                {
                    Code = $"TR-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                    SourceWarehouseId = dto.SourceWarehouseId,
                    DestinationWarehouseId = dto.DestinationWarehouseId,
                    CreatedByUserId = createdByUserId,
                    ExpectedDispatchDate = dto.ExpectedDispatchDate,
                    ExpectedReceiveDate = dto.ExpectedReceiveDate,
                    Note = dto.Note,
                    NotificationEmail = dto.NotificationEmail,
                    Status = StockTransferStatus.Draft,
                    Items = dto.Items.Select(i => new StockTransferItem
                    {
                        ProductId = i.ProductId,
                        MaterialId = i.MaterialId,
                        Quantity = i.Quantity
                    }).ToList()
                };

                _context.StockTransfers.Add(transfer);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Return basic info, client can call GetById for details
                transfer.SourceWarehouse = sourceWarehouse;
                transfer.DestinationWarehouse = destinationWarehouse;
                transfer.CreatedByUser = await _context.Users.FindAsync(createdByUserId) ?? new User();

                // Gửi email thông báo nếu có — PHẢI await (không fire-and-forget) và làm sau khi đã
                // commit: EmailService đọc cấu hình SMTP qua ISystemConfigService, dùng chung
                // ApplicationDbContext theo scope request với service này. Nếu không await, task gửi
                // email chạy song song với các câu lệnh khác trên cùng context/connection phía trên,
                // và vì connection string không bật MultipleActiveResultSets nên SQL Server ném lỗi
                // "The connection does not support MultipleActiveResultSets." Lỗi gửi email (SMTP down,
                // config sai...) không được làm hỏng phiếu đã tạo thành công nên bọc riêng try/catch.
                if (!string.IsNullOrWhiteSpace(dto.NotificationEmail))
                {
                    try
                    {
                        var staffName = "Nhân viên kho";
                        if (dto.AssignedStaffId.HasValue)
                        {
                            var staff = await _context.Users.FindAsync(dto.AssignedStaffId.Value);
                            if (staff != null) staffName = staff.FullName;
                        }

                        await _emailService.SendStockTransferNotificationAsync(
                            dto.NotificationEmail,
                            staffName,
                            transfer.Code,
                            sourceWarehouse.Name,
                            destinationWarehouse.Name,
                            dto.Note
                        );
                    }
                    catch
                    {
                        // Bỏ qua: phiếu điều chuyển đã tạo/commit thành công, chỉ email thông báo lỗi.
                    }
                }

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<StockTransferDto> UpdateAsync(Guid id, UpdateStockTransferDto dto)
        {
            var transfer = await _context.StockTransfers
                .Include(st => st.Items)
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.CreatedByUser)
                .FirstOrDefaultAsync(st => st.Id == id);

            if (transfer == null)
                throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

            if (transfer.Status != StockTransferStatus.Draft)
                throw new Exception("Chỉ có thể cập nhật phiếu ở trạng thái Nháp.");

            if (dto.ExpectedDispatchDate.HasValue && dto.ExpectedDispatchDate.Value < DateTime.UtcNow.AddMinutes(-5))
                throw new Exception("Thời gian xuất kho dự kiến không được nhỏ hơn thời gian hiện tại.");

            if (dto.ExpectedReceiveDate.HasValue && dto.ExpectedDispatchDate.HasValue && dto.ExpectedReceiveDate.Value < dto.ExpectedDispatchDate.Value)
                throw new Exception("Thời gian nhập kho dự kiến không được nhỏ hơn thời gian xuất kho dự kiến.");

            transfer.Note = dto.Note;
            transfer.ExpectedDispatchDate = dto.ExpectedDispatchDate;
            transfer.ExpectedReceiveDate = dto.ExpectedReceiveDate;

            foreach (var i in dto.Items)
            {
                if (i.ProductId == null && i.MaterialId == null)
                    throw new Exception("Mỗi mặt hàng điều chuyển phải chọn Thành phẩm hoặc Nguyên liệu.");
                if (i.ProductId != null && i.MaterialId != null)
                    throw new Exception("Chỉ được chọn 1 trong 2: Thành phẩm hoặc Nguyên liệu.");
            }

            // Update items
            _context.StockTransferItems.RemoveRange(transfer.Items);
            transfer.Items = dto.Items.Select(i => new StockTransferItem
            {
                StockTransferId = transfer.Id,
                ProductId = i.ProductId,
                MaterialId = i.MaterialId,
                Quantity = i.Quantity
            }).ToList();

            await _context.SaveChangesAsync();

            return MapToDto(transfer);
        }

        public async Task<StockTransferDto> DispatchAsync(Guid id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var transfer = await _context.StockTransfers
                    .Include(st => st.Items)
                    .Include(st => st.SourceWarehouse)
                    .Include(st => st.DestinationWarehouse)
                    .Include(st => st.CreatedByUser)
                    .FirstOrDefaultAsync(st => st.Id == id);

                if (transfer == null)
                    throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

                if (transfer.Status != StockTransferStatus.Draft && transfer.Status != StockTransferStatus.TransportArranged)
                    throw new Exception("Chỉ có thể xuất kho cho phiếu ở trạng thái Nháp hoặc Đã xếp xe.");

                // Deduct from Source Warehouse
                foreach (var item in transfer.Items)
                {
                    // Lấy toàn bộ tồn kho của sản phẩm/nguyên liệu ở kho xuất, trải trên mọi vị trí.
                    List<Inventory> inventories;
                    if (item.ProductId != null)
                    {
                        inventories = await _context.Inventories
                            .Include(i => i.WarehouseLocation)
                            .Where(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.ProductId == item.ProductId)
                            .ToListAsync();
                    }
                    else
                    {
                        inventories = await _context.Inventories
                            .Include(i => i.WarehouseLocation)
                            .Where(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.MaterialId == item.MaterialId)
                            .ToListAsync();
                    }

                    // BUGFIX: trước đây chỉ chấp nhận 1 vị trí đơn lẻ đủ số lượng (FirstOrDefault), nên
                    // hàng tồn nằm rải ở nhiều vị trí (rất phổ biến sau nhiều lần nhập kho) bị báo thiếu
                    // hàng oan dù tổng tồn kho thực tế vẫn đủ. Nay cộng dồn và trừ dần qua nhiều vị trí.
                    var totalAvailable = inventories.Sum(i => i.AvailableQuantity);
                    if (totalAvailable < item.Quantity)
                    {
                        var itemName = item.ProductId != null ? $"sản phẩm ID {item.ProductId}" : $"nguyên liệu ID {item.MaterialId}";
                        throw new Exception($"Không đủ hàng trong kho xuất cho {itemName}. Cần {item.Quantity}, tồn khả dụng {totalAvailable}.");
                    }

                    var remaining = item.Quantity;
                    foreach (var inv in inventories.Where(i => i.AvailableQuantity > 0).OrderByDescending(i => i.AvailableQuantity))
                    {
                        if (remaining <= 0) break;
                        var take = Math.Min(remaining, inv.AvailableQuantity);

                        // Trừ tồn kho và cộng vào hàng đi đường
                        inv.OnHandQuantity -= take;
                        inv.InTransitQuantity += take;
                        remaining -= take;

                        // GH-06/BR-035: mọi biến động tồn phải để lại vết StockTransaction (append-only).
                        _context.StockTransactions.Add(new StockTransaction
                        {
                            InventoryId = inv.Id,
                            ProductId = item.ProductId,
                            MaterialId = item.MaterialId,
                            WarehouseLocationId = inv.WarehouseLocationId,
                            QuantityChange = -take,
                            TransactionType = TransactionType.StockAdjustment,
                            ReferenceId = transfer.Id,
                            Note = $"Xuất điều chuyển {transfer.Code} sang kho {transfer.DestinationWarehouse.Name}",
                            CreatedByUserId = transfer.CreatedByUserId,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                transfer.Status = StockTransferStatus.Dispatched;
                transfer.DispatchedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // BR-022: xuất kho điều chuyển làm giảm OnHandQuantity thật -> bắt buộc audit trail.
                var dispatchActor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == transfer.CreatedByUserId);
                await _auditLogService.LogAsync(
                    entityName: "StockTransfer",
                    entityId: transfer.Id.ToString(),
                    action: "DISPATCH",
                    actorUserId: transfer.CreatedByUserId,
                    actorEmail: dispatchActor?.Email,
                    actorRole: "WarehouseStaff",
                    before: new { Status = "Draft/TransportArranged", transfer.Code },
                    after: new { Status = "Dispatched", transfer.Code, transfer.SourceWarehouseId, transfer.DestinationWarehouseId },
                    reason: $"Xuất kho điều chuyển {transfer.Code}");

                // Phiếu đã chuyển trạng thái Dispatched và commit thành công ở trên -> lỗi gửi
                // notification không được làm fail request, chỉ log để theo dõi.
                try
                {
                    var destinationStaffIds = await _context.Users
                        .Where(u => u.Role == SystemRole.WarehouseStaff && u.AssignedWarehouseId == transfer.DestinationWarehouseId)
                        .Select(u => u.Id)
                        .ToListAsync();

                    foreach (var staffId in destinationStaffIds)
                    {
                        await _notificationService.CreateNotificationAsync(
                            NotificationType.SYS_10_StockTransferDispatched,
                            staffId,
                            "Phiếu điều chuyển kho đang đến",
                            $"Phiếu điều chuyển {transfer.Code} từ kho {transfer.SourceWarehouse.Name} đã xuất kho, đang chuyển tới kho {transfer.DestinationWarehouse.Name}.",
                            transfer.Id,
                            "StockTransfer"
                        );
                    }
                }
                catch (Exception notifyEx)
                {
                    Console.WriteLine($"[StockTransferService] Error sending stock transfer dispatched notification: {notifyEx.Message}");
                }

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<StockTransferDto> ReceiveAsync(Guid id, ReceiveStockTransferDto dto, Guid staffId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var transfer = await _context.StockTransfers
                    .Include(st => st.Items)
                    .Include(st => st.SourceWarehouse)
                    .Include(st => st.DestinationWarehouse)
                    .Include(st => st.CreatedByUser)
                    .FirstOrDefaultAsync(st => st.Id == id);

                if (transfer == null)
                    throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

                // IDOR: WarehouseStaff chỉ được nhận hàng ở đúng kho được gán, không phải kho đích
                // của phiếu bất kỳ (cùng cơ chế với InventoryService.AdjustInventoryAsync).
                await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                    staffId, transfer.DestinationWarehouseId,
                    "nhận hàng điều chuyển", "StockTransfer", transfer.Id.ToString());

                if (transfer.Status != StockTransferStatus.Dispatched)
                    throw new Exception("Chỉ có thể nhận hàng cho phiếu đang ở trạng thái Đang giao (Dispatched).");

                var destLocation = await _context.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.WarehouseId == transfer.DestinationWarehouseId);

                if (destLocation == null)
                    throw new Exception("Kho nhập không có vị trí nào được thiết lập.");

                List<ReceiveStockTransferItemDto> parsedItems = new();
                if (!string.IsNullOrEmpty(dto.ItemsJson))
                {
                    parsedItems = System.Text.Json.JsonSerializer.Deserialize<List<ReceiveStockTransferItemDto>>(dto.ItemsJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ReceiveStockTransferItemDto>();
                }

                foreach (var rcvItem in parsedItems)
                {
                    StockTransferItem? transferItem;
                    if (rcvItem.ProductId != null)
                        transferItem = transfer.Items.FirstOrDefault(i => i.ProductId == rcvItem.ProductId);
                    else
                        transferItem = transfer.Items.FirstOrDefault(i => i.MaterialId == rcvItem.MaterialId);
                    
                    if (transferItem == null)
                    {
                        var itemName = rcvItem.ProductId != null ? $"sản phẩm ID {rcvItem.ProductId}" : $"nguyên liệu ID {rcvItem.MaterialId}";
                        throw new Exception($"{itemName} không có trong phiếu điều chuyển.");
                    }

                    if (rcvItem.ReceivedQuantity < 0)
                    {
                        var itemName = rcvItem.ProductId != null ? $"sản phẩm ID {rcvItem.ProductId}" : $"nguyên liệu ID {rcvItem.MaterialId}";
                        throw new Exception($"Số lượng nhận cho {itemName} không được âm.");
                    }

                    if (rcvItem.ReceivedQuantity > transferItem.Quantity)
                    {
                        var itemName = rcvItem.ProductId != null ? $"sản phẩm ID {rcvItem.ProductId}" : $"nguyên liệu ID {rcvItem.MaterialId}";
                        throw new Exception($"Số lượng nhận cho {itemName} ({rcvItem.ReceivedQuantity}) không được vượt quá số lượng đã xuất ({transferItem.Quantity}).");
                    }

                    transferItem.ReceivedQuantity = rcvItem.ReceivedQuantity;

                    // Giảm InTransit của kho xuất — BUGFIX: trước đây chỉ trừ vào 1 dòng tồn kho đầu
                    // tiên tìm thấy (FirstOrDefault), nhưng từ khi Dispatch có thể xuất hàng từ NHIỀU
                    // vị trí (xem DispatchAsync), phải trừ đúng những dòng đã thực sự được cộng InTransit
                    // lúc xuất kho -> tra lại đúng các StockTransaction (chân vết) đã ghi khi Dispatch.
                    var dispatchLegs = await _context.StockTransactions
                        .Where(t => t.ReferenceId == transfer.Id
                                 && t.TransactionType == TransactionType.StockAdjustment
                                 && t.QuantityChange < 0
                                 && (rcvItem.ProductId != null ? t.ProductId == rcvItem.ProductId : t.MaterialId == rcvItem.MaterialId))
                        .ToListAsync();

                    foreach (var leg in dispatchLegs)
                    {
                        var sourceInv = await _context.Inventories.FindAsync(leg.InventoryId);
                        if (sourceInv != null)
                        {
                            var takeBack = -leg.QuantityChange;
                            sourceInv.InTransitQuantity -= takeBack;
                            if (sourceInv.InTransitQuantity < 0) sourceInv.InTransitQuantity = 0;
                        }
                    }

                    // Tăng OnHand của kho nhập
                    Inventory? destInv;
                    if (rcvItem.ProductId != null)
                    {
                        destInv = await _context.Inventories
                            .FirstOrDefaultAsync(i => i.WarehouseLocationId == destLocation.Id && i.ProductId == rcvItem.ProductId);
                    }
                    else
                    {
                        destInv = await _context.Inventories
                            .FirstOrDefaultAsync(i => i.WarehouseLocationId == destLocation.Id && i.MaterialId == rcvItem.MaterialId);
                    }

                    if (destInv == null)
                    {
                        destInv = new Inventory
                        {
                            ProductId = rcvItem.ProductId,
                            MaterialId = rcvItem.MaterialId,
                            WarehouseLocationId = destLocation.Id,
                            OnHandQuantity = rcvItem.ReceivedQuantity
                        };
                        _context.Inventories.Add(destInv);
                    }
                    else
                    {
                        destInv.OnHandQuantity += rcvItem.ReceivedQuantity;
                    }

                    // GH-06/BR-035: mọi biến động tồn phải để lại vết StockTransaction (append-only).
                    _context.StockTransactions.Add(new StockTransaction
                    {
                        InventoryId = destInv.Id,
                        ProductId = rcvItem.ProductId,
                        MaterialId = rcvItem.MaterialId,
                        WarehouseLocationId = destLocation.Id,
                        QuantityChange = rcvItem.ReceivedQuantity,
                        TransactionType = TransactionType.StockAdjustment,
                        ReferenceId = transfer.Id,
                        Note = $"Nhận điều chuyển {transfer.Code} từ kho {transfer.SourceWarehouse.Name}",
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                transfer.Status = StockTransferStatus.Received;
                transfer.ReceivedAt = DateTime.UtcNow;
                transfer.ReceiveNote = dto.Note;
                
                if (dto.ProofImages != null && dto.ProofImages.Count > 0)
                {
                    var uploadedUrls = new List<string>();
                    foreach (var file in dto.ProofImages)
                    {
                        var url = await _cloudinaryService.UploadImageAsync(file, "viettien/stock-transfers");
                        uploadedUrls.Add(url);
                    }
                    transfer.ProofImageUrl = string.Join(",", uploadedUrls);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var receiveActor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId);
                await _auditLogService.LogAsync(
                    entityName: "StockTransfer",
                    entityId: transfer.Id.ToString(),
                    action: "RECEIVE",
                    actorUserId: staffId,
                    actorEmail: receiveActor?.Email,
                    actorRole: "WarehouseStaff",
                    before: new { Status = "Dispatched", transfer.Code },
                    after: new { Status = "Received", transfer.Code },
                    reason: dto.Note);

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<StockTransferDto> CancelAsync(Guid id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var transfer = await _context.StockTransfers
                    .Include(st => st.Items)
                    .Include(st => st.SourceWarehouse)
                    .Include(st => st.DestinationWarehouse)
                    .Include(st => st.CreatedByUser)
                    .FirstOrDefaultAsync(st => st.Id == id);

                if (transfer == null)
                    throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

                if (transfer.Status == StockTransferStatus.Received || transfer.Status == StockTransferStatus.Cancelled)
                    throw new Exception("Không thể hủy phiếu đã hoàn thành hoặc đã hủy.");

                if (transfer.Status == StockTransferStatus.Dispatched)
                {
                    // Hoàn lại tồn kho cho kho xuất — BUGFIX: trước đây chỉ hoàn vào 1 dòng tồn kho đầu
                    // tiên tìm thấy, không khớp với các vị trí thực sự đã bị trừ lúc Dispatch (nay có thể
                    // trải nhiều vị trí) -> tra lại đúng StockTransaction đã ghi khi Dispatch và hoàn đúng
                    // từng dòng tồn kho đó.
                    var dispatchLegs = await _context.StockTransactions
                        .Where(t => t.ReferenceId == transfer.Id
                                 && t.TransactionType == TransactionType.StockAdjustment
                                 && t.QuantityChange < 0)
                        .ToListAsync();

                    foreach (var leg in dispatchLegs)
                    {
                        var sourceInv = await _context.Inventories.FindAsync(leg.InventoryId);
                        if (sourceInv == null) continue;

                        var takeBack = -leg.QuantityChange;
                        sourceInv.InTransitQuantity -= takeBack;
                        if (sourceInv.InTransitQuantity < 0) sourceInv.InTransitQuantity = 0;
                        sourceInv.OnHandQuantity += takeBack;

                        // GH-06/BR-035: mọi biến động tồn phải để lại vết StockTransaction (append-only).
                        _context.StockTransactions.Add(new StockTransaction
                        {
                            InventoryId = sourceInv.Id,
                            ProductId = leg.ProductId,
                            MaterialId = leg.MaterialId,
                            WarehouseLocationId = sourceInv.WarehouseLocationId,
                            QuantityChange = takeBack,
                            TransactionType = TransactionType.StockAdjustment,
                            ReferenceId = transfer.Id,
                            Note = $"Hủy điều chuyển {transfer.Code}, hoàn tồn kho xuất",
                            CreatedByUserId = transfer.CreatedByUserId,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                transfer.Status = StockTransferStatus.Cancelled;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Gửi sau khi đã commit và PHẢI await (không fire-and-forget) — cùng lý do MARS đã nêu
                // ở CreateAsync: EmailService dùng chung ApplicationDbContext theo scope request, chạy
                // song song với SaveChangesAsync/CommitAsync phía trên sẽ vỡ "MultipleActiveResultSets".
                if (!string.IsNullOrEmpty(transfer.NotificationEmail))
                {
                    try
                    {
                        await _emailService.SendGenericEmailAsync(
                            transfer.NotificationEmail,
                            $"[Hủy Lệnh] Lệnh điều chuyển kho {transfer.Code} đã bị hủy",
                            $"Lệnh điều chuyển kho có mã {transfer.Code} từ {transfer.SourceWarehouse?.Name} đến {transfer.DestinationWarehouse?.Name} đã bị hủy."
                        );
                    }
                    catch
                    {
                        // Bỏ qua: phiếu đã hủy/commit thành công, chỉ email thông báo lỗi.
                    }
                }

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<StockTransferDto> RequestTransportAsync(Guid id)
        {
            var transfer = await _context.StockTransfers
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.CreatedByUser)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Product)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(st => st.Id == id);

            if (transfer == null)
                throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");

            if (transfer.Status != StockTransferStatus.Draft)
                throw new Exception("Chỉ có thể gửi yêu cầu xếp xe cho phiếu ở trạng thái Nháp.");

            transfer.Status = StockTransferStatus.TransportRequested;
            await _context.SaveChangesAsync();

            return MapToDto(transfer);
        }

        private static StockTransferDto MapToDto(StockTransfer st)
        {
            return new StockTransferDto
            {
                Id = st.Id,
                Code = st.Code,
                SourceWarehouseId = st.SourceWarehouseId,
                SourceWarehouseName = st.SourceWarehouse?.Name ?? "",
                DestinationWarehouseId = st.DestinationWarehouseId,
                DestinationWarehouseName = st.DestinationWarehouse?.Name ?? "",
                CreatedByUserId = st.CreatedByUserId,
                CreatedByUserName = st.CreatedByUser?.FullName ?? "",
                ExpectedDispatchDate = st.ExpectedDispatchDate,
                ExpectedReceiveDate = st.ExpectedReceiveDate,
                Status = st.Status,
                CreatedAt = st.CreatedAt,
                DispatchedAt = st.DispatchedAt,
                ReceivedAt = st.ReceivedAt,
                Note = st.Note,
                ReceiveNote = st.ReceiveNote,
                ProofImageUrl = st.ProofImageUrl,
                NotificationEmail = st.NotificationEmail,
                Items = st.Items.Select(i => new StockTransferItemDto 
                { 
                    Id = i.Id,
                    StockTransferId = i.StockTransferId,
                    ProductId = i.ProductId, 
                    MaterialId = i.MaterialId,
                    ItemName = i.Product?.Name ?? i.Material?.Name ?? "N/A",
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    Quantity = i.Quantity,
                    ReceivedQuantity = i.ReceivedQuantity
                }).ToList(),
                DeliveryVehicleId = st.DeliveryVehicleId,
                DeliveryShift = st.DeliveryShift,
                ScheduledDeliveryDate = st.ScheduledDeliveryDate
            };
        }
    }
}
