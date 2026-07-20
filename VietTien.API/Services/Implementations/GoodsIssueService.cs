using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class GoodsIssueService : IGoodsIssueService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;

        public GoodsIssueService(ApplicationDbContext context, ICloudinaryService cloudinaryService)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
        }

        public async Task<IEnumerable<GoodsIssueDto>> GetGoodsIssuesAsync(string? type)
        {
            var query = _context.GoodsIssues
                .Include(gi => gi.Warehouse)
                .Include(gi => gi.IssuedByUser)
                .Include(gi => gi.Items).ThenInclude(i => i.Product)
                .Include(gi => gi.Items).ThenInclude(i => i.Material)
                .AsQueryable();

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<GoodsIssueType>(type, true, out var typeEnum))
            {
                query = query.Where(gi => gi.Type == typeEnum);
            }

            var issues = await query.OrderByDescending(gi => gi.CreatedAt).ToListAsync();

            return issues.Select(MapToDto);
        }

        public async Task<GoodsIssueDto> GetGoodsIssueByIdAsync(Guid id)
        {
            var issue = await _context.GoodsIssues
                .Include(gi => gi.Warehouse)
                .Include(gi => gi.IssuedByUser)
                .Include(gi => gi.Items).ThenInclude(i => i.Product)
                .Include(gi => gi.Items).ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(gi => gi.Id == id);

            if (issue == null) throw new Exception("Goods Issue not found");

            return MapToDto(issue);
        }

        public async Task<GoodsIssueDto> CreateGoodsIssueAsync(CreateGoodsIssueRequestDto request, Guid staffId)
        {
            var issue = new GoodsIssue
            {
                Code = $"GI-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}",
                Type = Enum.TryParse<GoodsIssueType>(request.Type, true, out var t) ? t : GoodsIssueType.Other,
                ReferenceId = request.ReferenceId,
                WarehouseId = request.WarehouseId,
                IssuedByUserId = staffId,
                Status = GoodsIssueStatus.Draft,
                Note = request.Note,
                CreatedAt = DateTime.UtcNow
            };

            if (issue.Type == GoodsIssueType.ProductionMaterial)
            {
                issue.Status = GoodsIssueStatus.ProofPending;
            }

            foreach (var item in request.Items)
            {
                // Validate: phải có đúng 1 trong 2
                if (item.ProductId == null && item.MaterialId == null)
                    throw new Exception("Mỗi dòng xuất kho phải có ProductId hoặc MaterialId.");
                if (item.ProductId != null && item.MaterialId != null)
                    throw new Exception("Mỗi dòng xuất kho chỉ được chọn 1 trong 2: ProductId hoặc MaterialId.");

                issue.Items.Add(new GoodsIssueItem
                {
                    ProductId = item.ProductId,
                    MaterialId = item.MaterialId,
                    Quantity = item.Quantity
                });
            }

            _context.GoodsIssues.Add(issue);
            await _context.SaveChangesAsync();

            return await GetGoodsIssueByIdAsync(issue.Id);
        }

        public async Task<GoodsIssueDto> UploadProofAsync(Guid issueId, IFormFile file)
        {
            var issue = await _context.GoodsIssues.FirstOrDefaultAsync(gi => gi.Id == issueId);
            if (issue == null) throw new Exception("Goods Issue not found");
            
            if (issue.Status != GoodsIssueStatus.ProofPending)
            {
                throw new Exception("Goods Issue is not in a state to accept proof.");
            }

            var uploadUrl = await _cloudinaryService.UploadEvidenceAsync(file, "GoodsIssues");
            issue.ImageProofUrl = uploadUrl;
            issue.Status = GoodsIssueStatus.ProofUploaded;

            await _context.SaveChangesAsync();

            return await GetGoodsIssueByIdAsync(issue.Id);
        }

        public async Task<GoodsIssueDto> PostGoodsIssueAsync(Guid issueId, Guid staffId)
        {
            var issue = await _context.GoodsIssues
                .Include(gi => gi.Items)
                .FirstOrDefaultAsync(gi => gi.Id == issueId);

            if (issue == null) throw new Exception("Goods Issue not found");

            if (issue.Type == GoodsIssueType.ProductionMaterial && string.IsNullOrEmpty(issue.ImageProofUrl))
            {
                throw new Exception("Production Material issue requires a signed proof to be uploaded before posting.");
            }

            if (issue.Status == GoodsIssueStatus.Posted || issue.Status == GoodsIssueStatus.Cancelled)
            {
                throw new Exception("Goods Issue cannot be posted in its current status.");
            }

            var defaultLocation = await _context.WarehouseLocations.FirstOrDefaultAsync(l => l.WarehouseId == issue.WarehouseId && l.Type == "Normal");
            if (defaultLocation == null) throw new Exception("Default normal location not found in the warehouse");

            foreach (var item in issue.Items)
            {
                Inventory? inventory;
                if (item.ProductId != null)
                {
                    inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                        inv.ProductId == item.ProductId && 
                        inv.WarehouseLocationId == defaultLocation.Id);
                }
                else
                {
                    inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                        inv.MaterialId == item.MaterialId && 
                        inv.WarehouseLocationId == defaultLocation.Id);
                }

                if (inventory == null || inventory.OnHandQuantity < item.Quantity)
                {
                    var itemName = item.ProductId != null ? $"sản phẩm ID {item.ProductId}" : $"nguyên liệu ID {item.MaterialId}";
                    throw new Exception($"Tồn kho không đủ cho {itemName}. Chỉ còn {(inventory?.OnHandQuantity ?? 0)}.");
                }

                // Deduct inventory
                inventory.OnHandQuantity -= item.Quantity;

                // Log StockTransaction
                var tx = new StockTransaction
                {
                    InventoryId = inventory.Id,
                    ProductId = item.ProductId,
                    MaterialId = item.MaterialId,
                    WarehouseLocationId = defaultLocation.Id,
                    QuantityChange = -item.Quantity, // Âm
                    TransactionType = TransactionType.GoodsIssue,
                    ReferenceId = issue.Id,
                    CreatedByUserId = staffId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockTransactions.Add(tx);
            }

            issue.Status = GoodsIssueStatus.Posted;
            issue.IssueDate = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            return await GetGoodsIssueByIdAsync(issue.Id);
        }

        private GoodsIssueDto MapToDto(GoodsIssue gi)
        {
            return new GoodsIssueDto
            {
                Id = gi.Id,
                Code = gi.Code,
                Type = gi.Type.ToString(),
                ReferenceId = gi.ReferenceId,
                WarehouseId = gi.WarehouseId,
                WarehouseName = gi.Warehouse?.Name ?? "",
                IssuedByUserId = gi.IssuedByUserId,
                IssuedByName = gi.IssuedByUser?.FullName ?? "",
                Status = gi.Status.ToString(),
                CreatedAt = gi.CreatedAt,
                IssueDate = gi.IssueDate,
                ImageProofUrl = gi.ImageProofUrl,
                Note = gi.Note,
                Items = gi.Items.Select(i => new GoodsIssueItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ItemName = i.Product?.Name ?? i.Material?.Name ?? "N/A",
                    ItemSku = i.Product?.Sku ?? "",
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    Unit = i.Product?.Unit ?? i.Material?.Unit ?? "",
                    Quantity = i.Quantity
                }).ToList()
            };
        }
    }
}
