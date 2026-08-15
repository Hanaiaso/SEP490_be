using System.Text;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class PurchaseOrderService : IPurchaseOrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<PurchaseOrderService> _logger;
        private readonly IOcrService _ocrService;

        public PurchaseOrderService(ApplicationDbContext context, INotificationService notificationService, ILogger<PurchaseOrderService> logger, IOcrService ocrService)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
            _ocrService = ocrService;
        }

        // So khớp gần đúng theo tên (không hoa-thường, chứa lẫn nhau 2 chiều) — dữ liệu OCR từ hóa đơn giấy
        // hiếm khi khớp tuyệt đối với tên trong catalogue hệ thống.
        private static T? FuzzyMatchByName<T>(IEnumerable<T> candidates, string? name, Func<T, string> nameSelector) where T : class
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var normalized = name.Trim().ToLowerInvariant();
            return candidates.FirstOrDefault(c =>
            {
                var cName = nameSelector(c).Trim().ToLowerInvariant();
                return cName == normalized || cName.Contains(normalized) || normalized.Contains(cName);
            });
        }

        public async Task<PurchaseOrderDto> CreateAsync(Guid ceoId, CreatePurchaseOrderRequest request)
        {
            var supplier = await _context.Suppliers.FindAsync(request.SupplierId);
            if (supplier == null) 
                throw new Exception("Nhà cung cấp không tồn tại trong hệ thống. Vui lòng chọn Nhà cung cấp hợp lệ.");

            var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
            if (warehouse == null)
            {
                warehouse = await _context.Warehouses.FirstOrDefaultAsync();
                if (warehouse == null)
                {
                    warehouse = new Warehouse
                    {
                        Code = "WH-PROD",
                        Name = "Kho Thành Phẩm (WH-PROD)"
                    };
                    _context.Warehouses.Add(warehouse);
                    await _context.SaveChangesAsync();
                }
            }

            if (request.Items == null || !request.Items.Any())
                throw new Exception("Vui lòng chọn ít nhất 1 sản phẩm hoặc nguyên liệu để tạo PO.");

            foreach (var item in request.Items)
            {
                // Validate: phải có đúng 1 trong 2
                if (item.ProductId == null && item.MaterialId == null)
                    throw new Exception("Mỗi dòng PO phải có ProductId hoặc MaterialId.");
                if (item.ProductId != null && item.MaterialId != null)
                    throw new Exception("Mỗi dòng PO chỉ được chọn 1 trong 2: ProductId hoặc MaterialId.");

                if (item.ProductId != null)
                {
                    var productExists = await _context.Products.AnyAsync(p => p.Id == item.ProductId);
                    if (!productExists)
                        throw new Exception($"Sản phẩm với ID '{item.ProductId}' không tồn tại trong hệ thống.");
                }
                if (item.MaterialId != null)
                {
                    var materialExists = await _context.Materials.AnyAsync(m => m.Id == item.MaterialId);
                    if (!materialExists)
                        throw new Exception($"Nguyên liệu với ID '{item.MaterialId}' không tồn tại trong hệ thống.");
                }
            }

            var code = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
            
            var po = new PurchaseOrder
            {
                Code = code,
                CreatedById = ceoId,
                SupplierId = supplier.Id,
                WarehouseId = warehouse.Id,
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = request.ExpectedDeliveryDate,
                Note = request.Note,
                DeliveryTerms = request.DeliveryTerms,
                Items = request.Items.Select(i => new PurchaseOrderItem
                {
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ExpectedQuantity = i.ExpectedQuantity,
                    UnitPrice = i.UnitPrice,
                    Unit = i.Unit,
                    Note = i.Note,
                    ReceivedQuantity = 0
                }).ToList()
            };

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();

            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> ImportFromExcelAsync(Microsoft.AspNetCore.Http.IFormFile file, Guid ceoId)
        {
            // GH-10/BR-015: đọc thật nội dung file .xlsx bằng ClosedXML (header dòng 1:
            // ProductSku,Quantity,UnitPrice) thay vì giả định CSV — file Excel thật là nhị phân,
            // không đọc được bằng StreamReader text thuần.
            var rows = new List<(string Sku, int Quantity, decimal UnitPrice)>();
            var rowErrors = new List<string>();

            ClosedXML.Excel.IXLWorksheet worksheet;
            using (var stream = file.OpenReadStream())
            {
                try
                {
                    using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                    worksheet = workbook.Worksheets.First();

                    var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    for (int rowIdx = 2; rowIdx <= lastRow; rowIdx++) // dòng 1 là header
                    {
                        var row = worksheet.Row(rowIdx);
                        if (row.IsEmpty()) continue;

                        var sku = row.Cell(1).GetString().Trim();
                        if (string.IsNullOrWhiteSpace(sku))
                        {
                            rowErrors.Add($"Dòng {rowIdx}: thiếu SKU sản phẩm.");
                            continue;
                        }
                        if (!row.Cell(2).TryGetValue<int>(out var qty) || qty <= 0)
                        {
                            rowErrors.Add($"Dòng {rowIdx}: số lượng không hợp lệ ('{row.Cell(2).GetString()}').");
                            continue;
                        }
                        if (!row.Cell(3).TryGetValue<decimal>(out var price) || price < 0)
                        {
                            rowErrors.Add($"Dòng {rowIdx}: đơn giá không hợp lệ ('{row.Cell(3).GetString()}').");
                            continue;
                        }
                        rows.Add((sku, qty, price));
                    }
                }
                catch (Exception)
                {
                    throw new Exception("Không đọc được file Excel. Vui lòng đảm bảo file đúng định dạng .xlsx.");
                }
            }

            if (rows.Count == 0)
                throw new Exception("Không đọc được dòng hàng hợp lệ nào từ file. " +
                    (rowErrors.Count > 0 ? string.Join(" ", rowErrors) : "File trống hoặc sai định dạng."));

            var supplierId = await _context.Suppliers.Select(s => s.Id).FirstOrDefaultAsync();
            if (supplierId == Guid.Empty)
                throw new InvalidOperationException("Hệ thống chưa có Nhà cung cấp nào, vui lòng tạo NCC trước khi import.");

            var warehouseId = await _context.Warehouses.Select(w => w.Id).FirstOrDefaultAsync();
            if (warehouseId == Guid.Empty)
                throw new InvalidOperationException("Hệ thống chưa có Kho nào, vui lòng tạo kho trước khi import.");

            var po = new PurchaseOrder
            {
                Code = "PO-EXCEL-" + DateTime.Now.Ticks.ToString().Substring(10),
                CreatedById = ceoId,
                SupplierId = supplierId,
                WarehouseId = warehouseId,
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Note = "Imported from Excel: " + file.FileName +
                    (rowErrors.Count > 0 ? $" | {rowErrors.Count} dòng lỗi bị bỏ qua: {string.Join(" ", rowErrors)}" : "")
            };

            foreach (var row in rows)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Sku == row.Sku);
                po.Items.Add(new PurchaseOrderItem
                {
                    ProductId = product?.Id,
                    ExpectedQuantity = row.Quantity,
                    UnitPrice = row.UnitPrice,
                    Note = product == null ? $"SKU từ file không khớp sản phẩm nào: {row.Sku}" : null
                });
            }

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> ImportFromImageAsync(Microsoft.AspNetCore.Http.IFormFile file, Guid ceoId)
        {
            if (file.Length <= 0)
                throw new ArgumentException("File ảnh rỗng.");

            InvoiceOcrResult ocr;
            await using (var stream = file.OpenReadStream())
            {
                ocr = await _ocrService.ExtractInvoiceAsync(stream, file.ContentType);
            }

            if (ocr.Items.Count == 0)
                throw new InvalidOperationException("Không đọc được dòng hàng nào từ ảnh. Vui lòng thử ảnh rõ nét hơn hoặc nhập thủ công.");

            var suppliers = await _context.Suppliers.ToListAsync();
            var products = await _context.Products.Where(p => !p.IsDiscontinued).ToListAsync();
            var materials = await _context.Materials.ToListAsync();

            var matchedSupplier = FuzzyMatchByName(suppliers, ocr.VendorName, s => s.Name);
            var supplier = matchedSupplier ?? suppliers.FirstOrDefault();
            if (supplier == null)
                throw new InvalidOperationException("Hệ thống chưa có Nhà cung cấp nào, vui lòng tạo NCC trước khi import.");

            var warehouse = await _context.Warehouses.FirstOrDefaultAsync();
            if (warehouse == null)
                throw new InvalidOperationException("Hệ thống chưa có Kho nào, vui lòng tạo kho trước khi import.");

            var items = new List<PurchaseOrderItem>();
            foreach (var line in ocr.Items)
            {
                var product = FuzzyMatchByName(products, line.Description, p => p.Name);
                var material = product == null ? FuzzyMatchByName(materials, line.Description, m => m.Name) : null;

                items.Add(new PurchaseOrderItem
                {
                    ProductId = product?.Id,
                    MaterialId = material?.Id,
                    ExpectedQuantity = Math.Max(1, (int)Math.Round(line.Quantity ?? 1)),
                    UnitPrice = line.UnitPrice ?? product?.StandardListedPrice ?? 0,
                    Unit = product?.Unit ?? material?.Unit ?? "Cái",
                    Note = (product == null && material == null) ? $"OCR: {line.Description}" : null
                });
            }

            var unmatchedCount = items.Count(i => i.ProductId == null && i.MaterialId == null);
            var noteBuilder = new StringBuilder($"Imported from OCR Image: {file.FileName}.");
            if (matchedSupplier == null)
                noteBuilder.Append($" NCC chưa xác định (OCR đọc được: \"{ocr.VendorName}\") — vui lòng kiểm tra lại.");
            if (unmatchedCount > 0)
                noteBuilder.Append($" Có {unmatchedCount} dòng hàng chưa khớp được sản phẩm/nguyên liệu trong hệ thống — vui lòng sửa PO Draft trước khi phát hành.");

            var po = new PurchaseOrder
            {
                Code = "PO-OCR-" + DateTime.Now.Ticks.ToString().Substring(10),
                CreatedById = ceoId,
                SupplierId = supplier.Id,
                WarehouseId = warehouse.Id,
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = ocr.DueDate ?? DateTime.UtcNow.AddDays(7),
                Note = noteBuilder.ToString(),
                Items = items
            };

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> UpdateDraftAsync(Guid id, Guid ceoId, CreatePurchaseOrderRequest request)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");
            
            if (po.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException("Can only update Draft Purchase Orders");

            po.SupplierId = request.SupplierId;
            po.WarehouseId = request.WarehouseId;
            po.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
            po.Note = request.Note;
            po.DeliveryTerms = request.DeliveryTerms;

            // Simple replace items for draft
            _context.PurchaseOrderItems.RemoveRange(po.Items);
            var newItems = request.Items.Select(i => new PurchaseOrderItem
            {
                ProductId = i.ProductId,
                MaterialId = i.MaterialId,
                ExpectedQuantity = i.ExpectedQuantity,
                UnitPrice = i.UnitPrice,
                Unit = i.Unit,
                Note = i.Note,
                ReceivedQuantity = 0,
                PurchaseOrderId = po.Id
            }).ToList();

            // PurchaseOrderItem.Id có default = Guid.NewGuid() ngay trong C# -> nếu chỉ gán
            // po.Items = newItems (dựa vào navigation fixup tự động của EF trên 1 entity CHA đã
            // được track sẵn, không phải Add() mới), EF Core thấy Id khác Guid.Empty nên đoán nhầm
            // đây là bản ghi ĐÃ TỒN TẠI cần UPDATE thay vì bản ghi mới cần INSERT — UPDATE 1 dòng
            // chưa từng có trong DB thì 0 dòng bị ảnh hưởng, EF báo nhầm thành
            // DbUpdateConcurrencyException dù chẳng có ai khác đụng vào cả. Phải AddRange() TƯỜNG
            // MINH để buộc EF đánh dấu đúng trạng thái Added.
            _context.PurchaseOrderItems.AddRange(newItems);
            po.Items = newItems;

            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> IssueAsync(Guid id, Guid ceoId)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException("Can only issue Draft Purchase Orders");

            // GH-10: dòng hàng import từ Excel (SKU không khớp) hoặc từ ảnh OCR (mô tả không fuzzy-match
            // được sản phẩm/nguyên liệu nào) vẫn được thêm vào PO Draft với ProductId/MaterialId = null
            // (hiển thị "N/A" ở FE) để CEO còn cơ hội sửa tay trước khi phát hành — nhưng trước đây
            // không có gì chặn phát hành PO còn dòng "N/A" này, nên nó trôi thẳng tới Issued ->
            // SentToWarehouse -> màn hình nhập kho, nơi Kho không có sản phẩm thật nào để ghi nhận số
            // lượng đã nhận. Chặn ngay tại bước phát hành, buộc phải sửa PO Draft trước.
            var unmatchedCount = po.Items.Count(i => i.ProductId == null && i.MaterialId == null);
            if (unmatchedCount > 0)
                throw new InvalidOperationException(
                    $"PO còn {unmatchedCount} dòng hàng chưa khớp được sản phẩm/nguyên liệu nào trong hệ thống (hiển thị N/A). " +
                    "Vui lòng sửa PO Draft để gán đúng sản phẩm/nguyên liệu (hoặc xoá dòng đó) trước khi phát hành.");

            po.Status = PurchaseOrderStatus.Issued;
            po.IssuedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> SendToWarehouseAsync(Guid id, Guid ceoId)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.Issued)
                throw new InvalidOperationException("Can only send Issued Purchase Orders to warehouse");

            po.Status = PurchaseOrderStatus.SentToWarehouse;

            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_18_POSentToWarehouse,
                    SystemRole.WarehouseStaff,
                    "PO mới được gửi tới kho",
                    $"Purchase Order {po.Code} vừa được gửi tới kho để chuẩn bị nhập hàng.",
                    po.Id,
                    "PurchaseOrder"
                );
            }
            catch (Exception ex)
            {
                // PO đã chuyển trạng thái SentToWarehouse thành công (đã commit) -> lỗi gửi thông báo
                // không được làm request báo lỗi cho client, chỉ log để theo dõi.
                _logger.LogError(ex, "Lỗi gửi thông báo PO {PoId} tới kho", po.Id);
            }

            return await GetByIdAsync(po.Id);
        }

        public async Task<IEnumerable<PurchaseOrderListDto>> GetAllAsync(string? statusFilter)
        {
            var query = _context.PurchaseOrders
                .Include(p => p.Supplier)
                .Include(p => p.Warehouse)
                .Include(p => p.Items)
                .AsQueryable();

            if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<PurchaseOrderStatus>(statusFilter, true, out var statusEnum))
            {
                query = query.Where(p => p.Status == statusEnum);
            }

            var pos = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

            return pos.Select(p => new PurchaseOrderListDto
            {
                Id = p.Id,
                Code = p.Code,
                Status = p.Status.ToString(),
                CreatedAt = p.CreatedAt,
                IssuedAt = p.IssuedAt,
                ExpectedDeliveryDate = p.ExpectedDeliveryDate,
                SupplierName = p.Supplier.Name,
                WarehouseName = p.Warehouse.Name,
                TotalItems = p.Items.Count,
                TotalExpectedQuantity = p.Items.Sum(i => i.ExpectedQuantity),
                TotalReceivedQuantity = p.Items.Sum(i => i.ReceivedQuantity)
            });
        }

        public async Task<PurchaseOrderDto> GetByIdAsync(Guid id)
        {
            var p = await _context.PurchaseOrders
                .Include(po => po.CreatedBy)
                .Include(po => po.Supplier)
                .Include(po => po.Warehouse)
                .Include(po => po.Items).ThenInclude(i => i.Product)
                .Include(po => po.Items).ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(po => po.Id == id);

            if (p == null) throw new KeyNotFoundException("Purchase Order not found");

            return new PurchaseOrderDto
            {
                Id = p.Id,
                Code = p.Code,
                Status = p.Status.ToString(),
                CreatedAt = p.CreatedAt,
                ExpectedDeliveryDate = p.ExpectedDeliveryDate,
                IssuedAt = p.IssuedAt,
                Note = p.Note,
                DeliveryTerms = p.DeliveryTerms,
                CreatedById = p.CreatedById,
                CreatedByName = p.CreatedBy.FullName,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier.Name,
                SupplierCode = p.Supplier.Code,
                WarehouseId = p.WarehouseId,
                WarehouseName = p.Warehouse.Name,
                Items = p.Items.Select(i => new PurchaseOrderItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ItemName = i.Product?.Name ?? i.Material?.Name ?? "N/A",
                    ItemSku = i.Product?.Sku ?? "",
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    ExpectedQuantity = i.ExpectedQuantity,
                    ReceivedQuantity = i.ReceivedQuantity,
                    UnitPrice = i.UnitPrice,
                    Unit = i.Unit,
                    Note = i.Note
                }).ToList()
            };
        }

        public async Task<PurchaseOrderDto> CancelAsync(Guid id, Guid ceoId)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status == PurchaseOrderStatus.PartiallyReceived || po.Status == PurchaseOrderStatus.FullyReceived || po.Status == PurchaseOrderStatus.Closed)
            {
                throw new InvalidOperationException("Cannot cancel PO that has been partially or fully received.");
            }

            po.Status = PurchaseOrderStatus.Cancelled;
            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> ResolveDiscrepancyAsync(Guid id, Guid ceoId, DiscrepancyResolutionRequest request)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.DiscrepancyReview)
                throw new InvalidOperationException("PO is not in DiscrepancyReview status");

            po.Note = (po.Note ?? "") + $"\n[Resolution: {request.ResolutionType}] {request.Reason}";

            switch (request.ResolutionType)
            {
                case "ReturnExcess":
                    await ReturnExcessToSupplierAsync(po, ceoId);
                    po.Status = PurchaseOrderStatus.Closed;
                    break;

                case "RequestSupplemental":
                    if (!po.Items.Any(i => i.ReceivedQuantity < i.ExpectedQuantity))
                        throw new InvalidOperationException("PO này không còn hàng thiếu cần giao bổ sung.");
                    // Giữ PO mở (không Closed) để Kho vẫn tạo được Goods Receipt mới cho phần còn thiếu —
                    // GoodsReceiptService.CreateFromPOAsync chấp nhận PO ở SentToWarehouse hoặc PartiallyReceived.
                    po.Status = PurchaseOrderStatus.PartiallyReceived;
                    break;

                case "AcceptExcess":
                case "CloseShort":
                default:
                    po.Status = PurchaseOrderStatus.Closed;
                    break;
            }

            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        // Trừ phần hàng thừa (ReturnExcess) khỏi Inventory.QuarantineQuantity và ghi log audit —
        // không đụng tới QuarantineLog/luồng dispatch cách ly của Kho vì 1 QuarantineLog gộp chung
        // Damaged+WrongItem+Excess vào 1 con số duy nhất, không tách được riêng phần thừa từ đó.
        private async Task ReturnExcessToSupplierAsync(PurchaseOrder po, Guid ceoId)
        {
            var excessLines = await _context.GoodsReceiptItems
                .Include(i => i.GoodsReceipt)
                .Include(i => i.PurchaseOrderItem)
                .Where(i => i.GoodsReceipt.PurchaseOrderId == po.Id
                    && i.GoodsReceipt.Status == GoodsReceiptStatus.Posted
                    && i.ExcessQuantity > 0)
                .ToListAsync();

            if (excessLines.Count == 0)
                throw new InvalidOperationException("Không có hàng thừa nào cần trả lại NCC trên PO này.");

            var defaultLocation = await _context.WarehouseLocations
                .FirstOrDefaultAsync(l => l.WarehouseId == po.WarehouseId && l.Type == "Normal");
            if (defaultLocation == null)
                throw new Exception("Kho hàng của PO này chưa có vị trí lưu kho loại 'Normal'.");

            foreach (var line in excessLines)
            {
                var poItem = line.PurchaseOrderItem;
                Inventory? inventory = poItem.ProductId != null
                    ? await _context.Inventories.FirstOrDefaultAsync(inv => inv.ProductId == poItem.ProductId && inv.WarehouseLocationId == defaultLocation.Id)
                    : await _context.Inventories.FirstOrDefaultAsync(inv => inv.MaterialId == poItem.MaterialId && inv.WarehouseLocationId == defaultLocation.Id);

                if (inventory == null) continue;

                inventory.QuarantineQuantity = Math.Max(0, inventory.QuarantineQuantity - line.ExcessQuantity);

                _context.StockTransactions.Add(new StockTransaction
                {
                    InventoryId = inventory.Id,
                    ProductId = poItem.ProductId,
                    MaterialId = poItem.MaterialId,
                    WarehouseLocationId = defaultLocation.Id,
                    QuantityChange = -line.ExcessQuantity,
                    TransactionType = TransactionType.ReturnToSupplier,
                    ReferenceId = po.Id,
                    Note = $"Trả hàng thừa NCC cho PO {po.Code}",
                    CreatedByUserId = ceoId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        public async Task<PurchaseOrderDto> ClosePurchaseOrderAsync(Guid id, Guid ceoId)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.FullyReceived)
                throw new InvalidOperationException("Chỉ có thể đóng PO đã nhận đủ hàng (FullyReceived) và không còn sai lệch cần xử lý. " +
                    "PO đang ở trạng thái DiscrepancyReview phải qua ResolveDiscrepancy trước.");

            po.Status = PurchaseOrderStatus.Closed;
            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }
    }
}
