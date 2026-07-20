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

        public StockTransferService(ApplicationDbContext context, IEmailService emailService, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _emailService = emailService;
            _cloudinaryService = cloudinaryService;
        }

        public async Task<IEnumerable<StockTransferDto>> GetAllAsync()
        {
            var transfers = await _context.StockTransfers
                .Include(st => st.SourceWarehouse)
                .Include(st => st.DestinationWarehouse)
                .Include(st => st.CreatedByUser)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Product)
                .Include(st => st.Items)
                    .ThenInclude(i => i.Material)
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
                throw new Exception("Không tìm thấy phiếu điều chuyển.");

            var dto = MapToDto(transfer);
            return dto;
        }

        public async Task<StockTransferDto> CreateAsync(CreateStockTransferDto dto, Guid createdByUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sourceWarehouse = await _context.Warehouses.FindAsync(dto.SourceWarehouseId)
                    ?? throw new Exception("Không tìm thấy kho xuất.");
                
                var destinationWarehouse = await _context.Warehouses.FindAsync(dto.DestinationWarehouseId)
                    ?? throw new Exception("Không tìm thấy kho nhập.");

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

                // Gửi email thông báo nếu có
                if (!string.IsNullOrWhiteSpace(dto.NotificationEmail))
                {
                    var staffName = "Nhân viên kho";
                    if (dto.AssignedStaffId.HasValue)
                    {
                        var staff = await _context.Users.FindAsync(dto.AssignedStaffId.Value);
                        if (staff != null) staffName = staff.FullName;
                    }
                    
                    // Gửi email ngầm không await để không làm chậm request (hoặc await nếu muốn đảm bảo)
                    _ = _emailService.SendStockTransferNotificationAsync(
                        dto.NotificationEmail,
                        staffName,
                        transfer.Code,
                        sourceWarehouse.Name,
                        destinationWarehouse.Name,
                        dto.Note
                    );
                }

                await transaction.CommitAsync();
                
                // Return basic info, client can call GetById for details
                transfer.SourceWarehouse = sourceWarehouse;
                transfer.DestinationWarehouse = destinationWarehouse;
                transfer.CreatedByUser = await _context.Users.FindAsync(createdByUserId) ?? new User();
                
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
                throw new Exception("Không tìm thấy phiếu điều chuyển.");

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
                    throw new Exception("Không tìm thấy phiếu điều chuyển.");

                if (transfer.Status != StockTransferStatus.Draft)
                    throw new Exception("Chỉ có thể xuất kho cho phiếu ở trạng thái Nháp.");

                // Deduct from Source Warehouse
                foreach (var item in transfer.Items)
                {
                    // Lấy tồn kho của sản phẩm/nguyên liệu ở kho xuất (Lấy vị trí đầu tiên có hàng)
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

                    var availableInv = inventories.FirstOrDefault(i => i.AvailableQuantity >= item.Quantity);
                    if (availableInv == null)
                    {
                        var itemName = item.ProductId != null ? $"sản phẩm ID {item.ProductId}" : $"nguyên liệu ID {item.MaterialId}";
                        throw new Exception($"Không đủ hàng trong kho xuất cho {itemName}.");
                    }

                    // Trừ tồn kho và cộng vào hàng đi đường
                    availableInv.OnHandQuantity -= item.Quantity;
                    availableInv.InTransitQuantity += item.Quantity;
                }

                transfer.Status = StockTransferStatus.Dispatched;
                transfer.DispatchedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<StockTransferDto> ReceiveAsync(Guid id, ReceiveStockTransferDto dto)
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
                    throw new Exception("Không tìm thấy phiếu điều chuyển.");

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

                    transferItem.ReceivedQuantity = rcvItem.ReceivedQuantity;

                    // Giảm InTransit của kho xuất
                    Inventory? sourceInv;
                    if (rcvItem.ProductId != null)
                    {
                        sourceInv = await _context.Inventories
                            .Include(i => i.WarehouseLocation)
                            .FirstOrDefaultAsync(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.ProductId == rcvItem.ProductId);
                    }
                    else
                    {
                        sourceInv = await _context.Inventories
                            .Include(i => i.WarehouseLocation)
                            .FirstOrDefaultAsync(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.MaterialId == rcvItem.MaterialId);
                    }

                    if (sourceInv != null)
                    {
                        sourceInv.InTransitQuantity -= transferItem.Quantity;
                        if (sourceInv.InTransitQuantity < 0) sourceInv.InTransitQuantity = 0;
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
                    throw new Exception("Không tìm thấy phiếu điều chuyển.");

                if (transfer.Status == StockTransferStatus.Received || transfer.Status == StockTransferStatus.Cancelled)
                    throw new Exception("Không thể hủy phiếu đã hoàn thành hoặc đã hủy.");

                if (transfer.Status == StockTransferStatus.Dispatched)
                {
                    // Hoàn lại tồn kho cho kho xuất
                    foreach (var item in transfer.Items)
                    {
                        Inventory? sourceInv;
                        if (item.ProductId != null)
                        {
                            sourceInv = await _context.Inventories
                                .Include(i => i.WarehouseLocation)
                                .FirstOrDefaultAsync(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.ProductId == item.ProductId);
                        }
                        else
                        {
                            sourceInv = await _context.Inventories
                                .Include(i => i.WarehouseLocation)
                                .FirstOrDefaultAsync(i => i.WarehouseLocation.WarehouseId == transfer.SourceWarehouseId && i.MaterialId == item.MaterialId);
                        }

                        if (sourceInv != null)
                        {
                            sourceInv.InTransitQuantity -= item.Quantity;
                            if (sourceInv.InTransitQuantity < 0) sourceInv.InTransitQuantity = 0;
                            sourceInv.OnHandQuantity += item.Quantity;
                        }
                    }
                }

                transfer.Status = StockTransferStatus.Cancelled;

                if (!string.IsNullOrEmpty(transfer.NotificationEmail))
                {
                    _ = _emailService.SendGenericEmailAsync(
                        transfer.NotificationEmail, 
                        $"[Hủy Lệnh] Lệnh điều chuyển kho {transfer.Code} đã bị hủy", 
                        $"Lệnh điều chuyển kho có mã {transfer.Code} từ {transfer.SourceWarehouse?.Name} đến {transfer.DestinationWarehouse?.Name} đã bị hủy."
                    );
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return MapToDto(transfer);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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
                }).ToList()
            };
        }
    }
}
