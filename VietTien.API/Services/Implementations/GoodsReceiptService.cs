using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class GoodsReceiptService : IGoodsReceiptService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ILogger<GoodsReceiptService> _logger;
        private readonly IAuditLogService _auditLogService;

        public GoodsReceiptService(ApplicationDbContext context, INotificationService notificationService, ICloudinaryService cloudinaryService, ILogger<GoodsReceiptService> logger, IAuditLogService auditLogService)
        {
            _context = context;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
            _auditLogService = auditLogService;
        }

        public async Task<GoodsReceiptDto> CreateFromPOAsync(Guid poId, Guid warehouseStaffId, CreateGoodsReceiptRequest request)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == poId);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.SentToWarehouse && po.Status != PurchaseOrderStatus.PartiallyReceived)
            {
                throw new InvalidOperationException("Purchase Order is not ready to receive goods");
            }

            // ── Validate từng item trước khi tạo phiếu ──
            var errors = new List<string>();
            foreach (var item in request.Items)
            {
                var poItem = po.Items.FirstOrDefault(pi => pi.Id == item.PurchaseOrderItemId);
                if (poItem == null)
                {
                    errors.Add($"Không tìm thấy PO item {item.PurchaseOrderItemId}");
                    continue;
                }

                // Số lượng phải >= 0
                if (item.AcceptedQuantity < 0) errors.Add($"Đạt không được âm (item {poItem.Id})");
                if (item.DamagedQuantity < 0) errors.Add($"Hỏng không được âm (item {poItem.Id})");
                if (item.WrongItemQuantity < 0) errors.Add($"Sai loại không được âm (item {poItem.Id})");

                // Thừa và Thiếu không thể cùng > 0
                if (item.ExcessQuantity > 0 && item.ShortQuantity > 0)
                {
                    errors.Add($"Thừa và Thiếu không thể cùng lớn hơn 0 (item {poItem.Id})");
                }

                // Kiểm tra bất biến: Thực nhận = Đạt + Hỏng + Sai loại
                int actualReceived = item.AcceptedQuantity + item.DamagedQuantity + item.WrongItemQuantity;
                int remaining = Math.Max(0, poItem.ExpectedQuantity - poItem.ReceivedQuantity);

                // Excess phải = max(0, actualReceived - remaining)
                int expectedExcess = Math.Max(0, actualReceived - remaining);
                // Short phải = max(0, remaining - actualReceived)
                int expectedShort = Math.Max(0, remaining - actualReceived);

                if (item.ExcessQuantity != expectedExcess)
                {
                    errors.Add($"Thừa phải = {expectedExcess}, nhận được {item.ExcessQuantity} (item {poItem.Id})");
                }
                if (item.ShortQuantity != expectedShort)
                {
                    errors.Add($"Thiếu phải = {expectedShort}, nhận được {item.ShortQuantity} (item {poItem.Id})");
                }
            }

            if (errors.Count > 0)
            {
                throw new Exception("Dữ liệu kiểm đếm không hợp lệ:\n" + string.Join("\n", errors));
            }

            var receiptCode = $"GR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
            var receipt = new GoodsReceipt
            {
                PurchaseOrderId = poId,
                ReceivedByUserId = warehouseStaffId,
                Code = receiptCode,
                Status = GoodsReceiptStatus.Draft,
                Note = request.Note,
                Items = request.Items.Select(i => new GoodsReceiptItem
                {
                    PurchaseOrderItemId = i.PurchaseOrderItemId,
                    AcceptedQuantity = i.AcceptedQuantity,
                    DamagedQuantity = i.DamagedQuantity,
                    ExcessQuantity = i.ExcessQuantity,
                    ShortQuantity = i.ShortQuantity,
                    WrongItemQuantity = i.WrongItemQuantity,
                    BatchNumber = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    Note = i.Note
                }).ToList()
            };

            _context.GoodsReceipts.Add(receipt);
            await _context.SaveChangesAsync();

            return await GetReceiptDtoAsync(receipt.Id);
        }

        // BUGFIX: trước đây màn hình "Chi tiết phiếu nhập" (WarehouseGoodsReceipt.tsx) cho phép sửa số
        // lượng Đạt/Hỏng/Từ chối/Thực tế nhưng "Lưu nháp" chỉ đổi state React cục bộ — không hề gọi API
        // nào, nên mọi chỉnh sửa biến mất ngay khi tải lại trang, và "Hoàn tất nhập hàng" vẫn ghi sổ theo
        // đúng số liệu lúc TẠO phiếu ban đầu. Nay có endpoint thật để lưu chỉnh sửa khi phiếu còn Draft.
        // Thừa/Thiếu KHÔNG nhận trực tiếp từ client — tự tính lại theo đúng công thức CreateFromPOAsync
        // đã dùng, để không thể lệch do client tính sai.
        public async Task<GoodsReceiptDto> UpdateDraftReceiptAsync(Guid id, Guid warehouseStaffId, UpdateGoodsReceiptRequest request)
        {
            var receipt = await _context.GoodsReceipts
                .Include(r => r.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (receipt == null) throw new KeyNotFoundException("Goods Receipt not found");
            if (receipt.Status != GoodsReceiptStatus.Draft)
                throw new InvalidOperationException("Chỉ có thể sửa phiếu nhập khi còn ở trạng thái Nháp.");

            var errors = new List<string>();
            foreach (var itemReq in request.Items)
            {
                var receiptItem = receipt.Items.FirstOrDefault(i => i.PurchaseOrderItemId == itemReq.PurchaseOrderItemId);
                if (receiptItem == null)
                {
                    errors.Add($"Không tìm thấy dòng hàng {itemReq.PurchaseOrderItemId} trong phiếu này.");
                    continue;
                }

                // Cùng bất biến với CreateFromPOAsync: Thực nhận = Đạt + Hỏng + Sai loại.
                var poItem = receiptItem.PurchaseOrderItem;
                int actualReceived = itemReq.AcceptedQuantity + itemReq.DamagedQuantity + itemReq.WrongItemQuantity;
                int remaining = Math.Max(0, poItem.ExpectedQuantity - poItem.ReceivedQuantity);

                receiptItem.AcceptedQuantity = itemReq.AcceptedQuantity;
                receiptItem.DamagedQuantity = itemReq.DamagedQuantity;
                receiptItem.WrongItemQuantity = itemReq.WrongItemQuantity;
                receiptItem.ExcessQuantity = Math.Max(0, actualReceived - remaining);
                receiptItem.ShortQuantity = Math.Max(0, remaining - actualReceived);
            }

            if (errors.Count > 0)
            {
                throw new Exception("Dữ liệu kiểm đếm không hợp lệ:\n" + string.Join("\n", errors));
            }

            await _context.SaveChangesAsync();

            return await GetReceiptDtoAsync(receipt.Id);
        }

        public async Task<GoodsReceiptDto> PostReceiptAsync(Guid id, Guid warehouseStaffId)
        {
            var receipt = await _context.GoodsReceipts
                .Include(r => r.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (receipt == null) throw new KeyNotFoundException("Goods Receipt not found");
            if (receipt.Status != GoodsReceiptStatus.Draft) throw new InvalidOperationException("Goods Receipt is already posted or cancelled");

            bool hasSevereDiscrepancy = receipt.Items.Any(i => i.DamagedQuantity > 0 || i.WrongItemQuantity > 0);
            if (hasSevereDiscrepancy && string.IsNullOrEmpty(receipt.ImageProofUrl))
            {
                throw new Exception("Bắt buộc phải đính kèm tệp minh chứng (ảnh/biên bản) khi có hàng Hỏng hoặc Sai loại.");
            }

            receipt.Status = GoodsReceiptStatus.Posted;

            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == receipt.PurchaseOrderId);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            // Lấy trước vị trí lưu kho mặc định và chặn cứng nếu không có, tránh trường hợp
            // Posted nhưng tồn kho không được cộng do defaultLocation == null bị bỏ qua âm thầm.
            var defaultLocation = await _context.WarehouseLocations
                .FirstOrDefaultAsync(l => l.WarehouseId == po.WarehouseId && l.Type == "Normal");
            if (defaultLocation == null)
                throw new Exception("Kho hàng của PO này chưa có vị trí lưu kho loại 'Normal'. Vui lòng tạo vị trí lưu kho trước khi nhận hàng.");

            bool hasDiscrepancy = false;

            // Update PO Items received quantity
            foreach (var item in receipt.Items)
            {
                // Chỉ tính AcceptedQuantity vào ReceivedQuantity
                item.PurchaseOrderItem.ReceivedQuantity += item.AcceptedQuantity;

                if (item.DamagedQuantity > 0 || item.ExcessQuantity > 0 || item.ShortQuantity > 0 || item.WrongItemQuantity > 0)
                {
                    hasDiscrepancy = true;
                }

                // Update Inventory logic (chỉ cộng AcceptedQuantity)
                int totalDefective = item.DamagedQuantity + item.WrongItemQuantity + item.ExcessQuantity;

                if (item.AcceptedQuantity > 0 || totalDefective > 0)
                {
                    // Lookup inventory by ProductId or MaterialId
                    Inventory? inventory;
                    if (item.PurchaseOrderItem.ProductId != null)
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => inv.ProductId == item.PurchaseOrderItem.ProductId && inv.WarehouseLocationId == defaultLocation.Id);
                    }
                    else
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => inv.MaterialId == item.PurchaseOrderItem.MaterialId && inv.WarehouseLocationId == defaultLocation.Id);
                    }

                    // BUGFIX: OnHandQuantity phải là TỔNG số lượng vật lý đã nhận (kể cả hàng đang cách ly
                    // chờ QA), vì AvailableQuantity = OnHand - Reserved - Allocated - Damaged - Quarantine
                    // coi Quarantine/Damaged là các "khoản giữ" trừ bớt từ OnHand, không phải cộng thêm.
                    // DispatchQuarantine (WarehouseManagementController) xác nhận đúng hợp đồng này: khi QA
                    // duyệt hàng cách ly là "còn dùng được", nó CHỈ giảm QuarantineQuantity (comment gốc:
                    // "AvailableQuantity tự động tăng") — không cộng gì vào OnHand. Trước đây chỉ cộng
                    // AcceptedQuantity vào OnHand, bỏ sót totalDefective, nên số hàng cách ly được duyệt
                    // "còn dùng được" biến mất khỏi hệ thống vĩnh viễn dù vẫn nằm thật trên kệ.
                    if (inventory == null)
                    {
                        inventory = new Inventory
                        {
                            ProductId = item.PurchaseOrderItem.ProductId,
                            MaterialId = item.PurchaseOrderItem.MaterialId,
                            WarehouseLocationId = defaultLocation.Id,
                            OnHandQuantity = item.AcceptedQuantity + totalDefective,
                            QuarantineQuantity = totalDefective
                        };
                        _context.Inventories.Add(inventory);
                    }
                    else
                    {
                        inventory.OnHandQuantity += item.AcceptedQuantity + totalDefective;
                        inventory.QuarantineQuantity += totalDefective;
                    }

                    // Log StockTransaction — QuantityChange phải khớp đúng phần OnHandQuantity vừa cộng ở
                    // trên (AcceptedQuantity + totalDefective), không chỉ AcceptedQuantity, nếu không vết
                    // audit (báo cáo xuất/nhập, "hàng chậm luân chuyển") sẽ thiếu đúng phần hàng cách ly.
                    // Điều kiện cũ "if (AcceptedQuantity > 0)" bỏ sót cả trường hợp nguyên lô đều lỗi/sai
                    // (AcceptedQuantity = 0) dù OnHand vẫn tăng — khối này luôn chạy vì đã ở trong nhánh
                    // "AcceptedQuantity > 0 || totalDefective > 0" phía trên.
                    var tx = new StockTransaction
                    {
                        Inventory = inventory,
                        ProductId = item.PurchaseOrderItem.ProductId,
                        MaterialId = item.PurchaseOrderItem.MaterialId,
                        WarehouseLocationId = defaultLocation.Id,
                        QuantityChange = item.AcceptedQuantity + totalDefective,
                        TransactionType = TransactionType.GoodsReceipt,
                        ReferenceId = receipt.Id,
                        CreatedByUserId = warehouseStaffId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockTransactions.Add(tx);

                    if (totalDefective > 0)
                    {
                        var quarantine = new QuarantineLog
                        {
                            QuarantineCode = $"QZ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}",
                            OrderId = null, // Lỗi từ PO, không có OrderId
                            GoodsReceiptItemId = item.Id,
                            Inventory = inventory,
                            ProductId = item.PurchaseOrderItem.ProductId,
                            MaterialId = item.PurchaseOrderItem.MaterialId,
                            Quantity = totalDefective,
                            Reason = $"Từ Phiếu Nhập {receipt.Code}. Hỏng: {item.DamagedQuantity}, Sai: {item.WrongItemQuantity}, Thừa: {item.ExcessQuantity}",
                            Status = QuarantineStatus.Waiting,
                            ReceivedByUserId = warehouseStaffId,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.QuarantineLogs.Add(quarantine);
                    }
                }
            }

            // Update PO Status
            bool allFullyReceived = po.Items.All(i => i.ReceivedQuantity >= i.ExpectedQuantity);
            
            if (hasDiscrepancy)
            {
                po.Status = PurchaseOrderStatus.DiscrepancyReview;
            }
            else if (allFullyReceived)
            {
                po.Status = PurchaseOrderStatus.FullyReceived;
            }
            else
            {
                po.Status = PurchaseOrderStatus.PartiallyReceived;
            }

            await _context.SaveChangesAsync();

            // BR-022: Post phiếu nhận hàng cộng thẳng vào OnHandQuantity (và có thể tạo cách ly) ->
            // bắt buộc audit trail, trước đây luồng nhập kho không ghi AuditLog nào cả.
            var receiptActor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == warehouseStaffId);
            await _auditLogService.LogAsync(
                entityName: "GoodsReceipt",
                entityId: receipt.Id.ToString(),
                action: "POST",
                actorUserId: warehouseStaffId,
                actorEmail: receiptActor?.Email,
                actorRole: "WarehouseStaff",
                before: new { Status = "Draft", receipt.Code },
                after: new { Status = "Posted", receipt.Code, PoStatus = po.Status.ToString() },
                reason: $"Nhận hàng cho PO {po.Code}");

            if (hasDiscrepancy)
            {
                try
                {
                    // SYS-19_GoodsReceiptDiscrepancy — gửi sau khi đã lưu thành công, tránh trường hợp
                    // CEO nhận được thông báo "có sai lệch cần xem xét" cho một phiếu nhập chưa thực sự Posted.
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_19_GoodsReceiptDiscrepancy,
                        SystemRole.CEO,
                        "Nhập kho có sai lệch",
                        $"Phiếu nhập kho {receipt.Code} (PO {po.Code}) có sai lệch. Cần CEO hoặc Quản lý xem xét.",
                        po.Id,
                        "PurchaseOrder"
                    );
                }
                catch (Exception ex)
                {
                    // Phiếu nhập đã Posted và commit thành công -> lỗi gửi thông báo không được làm
                    // request báo lỗi cho warehouse staff, chỉ log để theo dõi.
                    _logger.LogError(ex, "Lỗi gửi thông báo sai lệch cho GoodsReceipt {ReceiptId}", receipt.Id);
                }
            }

            return await GetReceiptDtoAsync(receipt.Id);
        }

        public async Task<IEnumerable<GoodsReceiptDto>> GetByPurchaseOrderIdAsync(Guid poId)
        {
            var receipts = await _context.GoodsReceipts
                .Where(r => r.PurchaseOrderId == poId)
                .ToListAsync();

            var dtoList = new List<GoodsReceiptDto>();
            foreach (var r in receipts)
            {
                dtoList.Add(await GetReceiptDtoAsync(r.Id));
            }
            return dtoList;
        }

        public async Task<IEnumerable<GoodsReceiptDto>> GetAllAsync(string? status)
        {
            var query = _context.GoodsReceipts.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<GoodsReceiptStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(r => r.Status == parsedStatus);
            }

            var receipts = await query.ToListAsync();
            var dtoList = new List<GoodsReceiptDto>();
            foreach (var r in receipts)
            {
                dtoList.Add(await GetReceiptDtoAsync(r.Id));
            }
            return dtoList;
        }

        public async Task<GoodsReceiptDto> UploadProofAsync(Guid receiptId, Microsoft.AspNetCore.Http.IFormFile file)
        {
            var receipt = await _context.GoodsReceipts.FirstOrDefaultAsync(gr => gr.Id == receiptId);
            if (receipt == null) throw new KeyNotFoundException("Goods Receipt not found");
            
            if (receipt.Status != GoodsReceiptStatus.Draft)
            {
                throw new InvalidOperationException("Chỉ có thể tải minh chứng lên khi phiếu nhận hàng đang ở trạng thái Draft.");
            }

            var uploadUrl = await _cloudinaryService.UploadEvidenceAsync(file, "GoodsReceipts");
            receipt.ImageProofUrl = uploadUrl;

            await _context.SaveChangesAsync();

            return await GetReceiptDtoAsync(receipt.Id);
        }

        private async Task<GoodsReceiptDto> GetReceiptDtoAsync(Guid id)
        {
            var r = await _context.GoodsReceipts
                .Include(gr => gr.PurchaseOrder)
                .Include(gr => gr.ReceivedByUser)
                .Include(gr => gr.Items).ThenInclude(i => i.PurchaseOrderItem).ThenInclude(poi => poi.Product)
                .Include(gr => gr.Items).ThenInclude(i => i.PurchaseOrderItem).ThenInclude(poi => poi.Material)
                .FirstOrDefaultAsync(gr => gr.Id == id);

            if (r == null) throw new KeyNotFoundException("Goods Receipt not found");

            return new GoodsReceiptDto
            {
                Id = r.Id,
                PurchaseOrderId = r.PurchaseOrderId,
                PurchaseOrderCode = r.PurchaseOrder.Code,
                ReceivedByUserId = r.ReceivedByUserId,
                ReceivedByUserName = r.ReceivedByUser.FullName,
                Code = r.Code,
                Status = r.Status.ToString(),
                ReceivedDate = r.ReceivedDate,
                Note = r.Note,
                ImageProofUrl = r.ImageProofUrl,
                Items = r.Items.Select(i => new GoodsReceiptItemDto
                {
                    Id = i.Id,
                    PurchaseOrderItemId = i.PurchaseOrderItemId,
                    ItemName = i.PurchaseOrderItem.Product?.Name ?? i.PurchaseOrderItem.Material?.Name ?? "N/A",
                    ItemSku = i.PurchaseOrderItem.Product?.Sku ?? "",
                    ItemType = i.PurchaseOrderItem.MaterialId != null ? "Material" : "Product",
                    AcceptedQuantity = i.AcceptedQuantity,
                    DamagedQuantity = i.DamagedQuantity,
                    ExcessQuantity = i.ExcessQuantity,
                    ShortQuantity = i.ShortQuantity,
                    WrongItemQuantity = i.WrongItemQuantity,
                    BatchNumber = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    Note = i.Note
                }).ToList()
            };
        }
    }
}
