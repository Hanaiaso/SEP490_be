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

        public GoodsReceiptService(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<GoodsReceiptDto> CreateFromPOAsync(Guid poId, Guid warehouseStaffId, CreateGoodsReceiptRequest request)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == poId);
            if (po == null) throw new Exception("Purchase Order not found");

            if (po.Status != PurchaseOrderStatus.SentToWarehouse && po.Status != PurchaseOrderStatus.PartiallyReceived)
            {
                throw new Exception("Purchase Order is not ready to receive goods");
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

        public async Task<GoodsReceiptDto> PostReceiptAsync(Guid id, Guid warehouseStaffId)
        {
            var receipt = await _context.GoodsReceipts
                .Include(r => r.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (receipt == null) throw new Exception("Goods Receipt not found");
            if (receipt.Status != GoodsReceiptStatus.Draft) throw new Exception("Goods Receipt is already posted or cancelled");

            receipt.Status = GoodsReceiptStatus.Posted;

            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == receipt.PurchaseOrderId);
            
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
                if (item.AcceptedQuantity > 0)
                {
                    var defaultLocation = await _context.WarehouseLocations.FirstOrDefaultAsync(l => l.WarehouseId == po.WarehouseId && l.Type == "Normal");
                    if (defaultLocation != null)
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

                        if (inventory == null)
                        {
                            inventory = new Inventory
                            {
                                ProductId = item.PurchaseOrderItem.ProductId,
                                MaterialId = item.PurchaseOrderItem.MaterialId,
                                WarehouseLocationId = defaultLocation.Id,
                                OnHandQuantity = item.AcceptedQuantity
                            };
                            _context.Inventories.Add(inventory);
                        }
                        else
                        {
                            inventory.OnHandQuantity += item.AcceptedQuantity;
                        }

                        // Log StockTransaction
                        var tx = new StockTransaction
                        {
                            Inventory = inventory,
                            ProductId = item.PurchaseOrderItem.ProductId,
                            MaterialId = item.PurchaseOrderItem.MaterialId,
                            WarehouseLocationId = defaultLocation.Id,
                            QuantityChange = item.AcceptedQuantity,
                            TransactionType = TransactionType.GoodsReceipt,
                            ReferenceId = receipt.Id,
                            CreatedByUserId = warehouseStaffId,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.StockTransactions.Add(tx);
                    }
                }

                // Log Quarantine cho hàng lỗi/thừa/sai
                int totalDefective = item.DamagedQuantity + item.WrongItemQuantity + item.ExcessQuantity;
                if (totalDefective > 0)
                {
                    var quarantine = new QuarantineLog
                    {
                        QuarantineCode = $"QZ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}",
                        OrderId = null, // Lỗi từ PO, không có OrderId
                        GoodsReceiptItemId = item.Id,
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

            // Update PO Status
            bool allFullyReceived = po.Items.All(i => i.ReceivedQuantity >= i.ExpectedQuantity);
            
            if (hasDiscrepancy)
            {
                po.Status = PurchaseOrderStatus.DiscrepancyReview;

                // SYS-19_GoodsReceiptDiscrepancy
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_19_GoodsReceiptDiscrepancy,
                    SystemRole.CEO,
                    "Nhập kho có sai lệch",
                    $"Phiếu nhập kho {receipt.Code} (PO {po.Code}) có sai lệch. Cần CEO hoặc Quản lý xem xét.",
                    po.Id,
                    "PurchaseOrder"
                );
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

        private async Task<GoodsReceiptDto> GetReceiptDtoAsync(Guid id)
        {
            var r = await _context.GoodsReceipts
                .Include(gr => gr.ReceivedByUser)
                .Include(gr => gr.Items).ThenInclude(i => i.PurchaseOrderItem).ThenInclude(poi => poi.Product)
                .Include(gr => gr.Items).ThenInclude(i => i.PurchaseOrderItem).ThenInclude(poi => poi.Material)
                .FirstOrDefaultAsync(gr => gr.Id == id);

            if (r == null) throw new Exception("Not found");

            return new GoodsReceiptDto
            {
                Id = r.Id,
                PurchaseOrderId = r.PurchaseOrderId,
                ReceivedByUserId = r.ReceivedByUserId,
                ReceivedByUserName = r.ReceivedByUser.FullName,
                Code = r.Code,
                Status = r.Status.ToString(),
                ReceivedDate = r.ReceivedDate,
                Note = r.Note,
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
