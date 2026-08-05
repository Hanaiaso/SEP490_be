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

        public PurchaseOrderService(ApplicationDbContext context, INotificationService notificationService, ILogger<PurchaseOrderService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
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
            // TODO: Use EPPlus or ClosedXML to parse Excel.
            // For now, returning a mock draft PO created from "Excel"
            
            var po = new PurchaseOrder
            {
                Code = "PO-EXCEL-" + DateTime.Now.Ticks.ToString().Substring(10),
                CreatedById = ceoId,
                SupplierId = _context.Suppliers.Select(s => s.Id).FirstOrDefault(), // Mock
                WarehouseId = _context.Warehouses.Select(w => w.Id).FirstOrDefault(), // Mock
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Note = "Imported from Excel: " + file.FileName
            };

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> ImportFromImageAsync(Microsoft.AspNetCore.Http.IFormFile file, Guid ceoId)
        {
            // TODO: Call Azure Form Recognizer or local AI OCR to extract data.
            // Returning a mock draft PO created from "Image OCR"

            var po = new PurchaseOrder
            {
                Code = "PO-OCR-" + DateTime.Now.Ticks.ToString().Substring(10),
                CreatedById = ceoId,
                SupplierId = _context.Suppliers.Select(s => s.Id).FirstOrDefault(), // Mock
                WarehouseId = _context.Warehouses.Select(w => w.Id).FirstOrDefault(), // Mock
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7),
                Note = "Imported from OCR Image: " + file.FileName
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
            po.Items = request.Items.Select(i => new PurchaseOrderItem
            {
                ProductId = i.ProductId,
                MaterialId = i.MaterialId,
                ExpectedQuantity = i.ExpectedQuantity,
                UnitPrice = i.UnitPrice,
                Unit = i.Unit,
                Note = i.Note,
                ReceivedQuantity = 0
            }).ToList();

            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
        }

        public async Task<PurchaseOrderDto> IssueAsync(Guid id, Guid ceoId)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.Draft)
                throw new InvalidOperationException("Can only issue Draft Purchase Orders");

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
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) throw new KeyNotFoundException("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.DiscrepancyReview)
                throw new InvalidOperationException("PO is not in DiscrepancyReview status");

            // Logic to resolve: Add note and close it or keep it open.
            // Simplified: Add note to PO and close it.
            po.Note = (po.Note ?? "") + $"\n[Resolution: {request.ResolutionType}] {request.Reason}";
            po.Status = PurchaseOrderStatus.Closed;

            await _context.SaveChangesAsync();
            return await GetByIdAsync(po.Id);
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
