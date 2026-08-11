using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Product;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class ProductService : IProductService
    {
        private static readonly OrderStatus[] CancelledStatuses =
        {
            OrderStatus.Cancelled, OrderStatus.CancelledReallocated
        };

        private const int TopListLimit = 10;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly ApplicationDbContext _context;

        public ProductService(IUnitOfWork unitOfWork, ICloudinaryService cloudinaryService, ApplicationDbContext context)
        {
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
            _context = context;
        }

        public async Task<ProductPagedResultDto> GetProductsAsync(
            int page = 1,
            int pageSize = 12,
            Guid? categoryId = null,
            string? searchKeyword = null,
            string? sortBy = null)
        {
            // Đảm bảo giá trị hợp lệ
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 12 : (pageSize > 100 ? 100 : pageSize);

            var (items, totalCount) = await _unitOfWork.Products.GetAllAsync(
                page, pageSize, categoryId, searchKeyword, sortBy);

            var dtos = items.Select(p => new ProductSummaryDto
            {
                Id                  = p.Id,
                Name                = p.Name,
                Sku                 = p.Sku,
                StandardListedPrice = p.StandardListedPrice,
                ImageUrl            = p.ImageUrl,
                CategoryId          = p.CategoryId,
                CategoryName        = p.Category.Name,
                AvailableStock      = p.Inventories?.Sum(i => i.AvailableQuantity) ?? 0,
                AverageRating       = p.AverageRating,
                ReviewCount         = p.ReviewCount
            });

            return new ProductPagedResultDto
            {
                Items      = dtos,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            };
        }

        public async Task<ProductDetailDto?> GetProductByIdAsync(Guid id)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(id);

            if (product is null)
                return null;

            return new ProductDetailDto
            {
                Id                  = product.Id,
                Name                = product.Name,
                Sku                 = product.Sku,
                StandardListedPrice = product.StandardListedPrice,
                Description         = product.Description,
                Specifications      = product.Specifications,
                ImageUrl            = product.ImageUrl,
                CategoryId          = product.CategoryId,
                CategoryName        = product.Category.Name,
                PhysicalStock       = product.Inventories?.Sum(i => i.OnHandQuantity) ?? 0,
                AvailableStock      = product.Inventories?.Sum(i => i.AvailableQuantity) ?? 0,
                AverageRating       = product.AverageRating,
                ReviewCount         = product.ReviewCount
            };
        }

        public async Task<IEnumerable<CategoryDto>> GetCategoriesAsync()
        {
            var categories = await _unitOfWork.Products.GetActiveCategoriesAsync();

            return categories.Select(c => new CategoryDto
            {
                Id          = c.Id,
                Name        = c.Name,
                Description = c.Description
            });
        }

        public async Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                throw new Exception("Danh mục không tồn tại.");

            if (await _context.Products.AnyAsync(p => p.Sku == dto.Sku))
                throw new Exception($"SKU '{dto.Sku}' đã được sử dụng bởi sản phẩm khác.");

            string? imageUrl = null;
            if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                imageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "products");
            }

            var newProduct = new VietTien.API.Models.Product
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Sku = dto.Sku,
                StandardListedPrice = dto.StandardListedPrice,
                CategoryId = dto.CategoryId,
                Unit = dto.Unit,
                Description = dto.Description,
                Specifications = dto.Specifications,
                ImageUrl = imageUrl,
                IsDiscontinued = false
            };

            await _unitOfWork.Products.AddAsync(newProduct);
            await _unitOfWork.SaveChangesAsync();

            return await GetProductByIdAsync(newProduct.Id) ?? throw new Exception("Không thể lấy thông tin sản phẩm sau khi tạo.");
        }

        public async Task<ProductManagementPagedResultDto> GetProductsForManagementAsync(
            int page = 1,
            int pageSize = 12,
            Guid? categoryId = null,
            string? searchKeyword = null,
            bool? isDiscontinued = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 12 : (pageSize > 100 ? 100 : pageSize);

            var (items, totalCount) = await _unitOfWork.Products.GetAllForManagementAsync(
                page, pageSize, categoryId, searchKeyword, isDiscontinued);

            var productList = items.ToList();
            var productIds = productList.Select(p => p.Id).ToList();

            var soldByProduct = await _context.OrderItems
                .AsNoTracking()
                .Where(oi => productIds.Contains(oi.ProductId) && !CancelledStatuses.Contains(oi.Order.OrderStatus))
                .GroupBy(oi => oi.ProductId)
                .Select(g => new { ProductId = g.Key, UnitsSold = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.UnitsSold);

            var dtos = productList.Select(p => new ProductManagementItemDto
            {
                Id                  = p.Id,
                Name                = p.Name,
                Sku                 = p.Sku,
                StandardListedPrice = p.StandardListedPrice,
                ImageUrl            = p.ImageUrl,
                CategoryId          = p.CategoryId,
                CategoryName        = p.Category.Name,
                Unit                = p.Unit,
                AvailableStock      = p.Inventories?.Sum(i => i.AvailableQuantity) ?? 0,
                IsDiscontinued      = p.IsDiscontinued,
                AverageRating       = p.AverageRating,
                ReviewCount         = p.ReviewCount,
                UnitsSoldTotal      = soldByProduct.TryGetValue(p.Id, out var sold) ? sold : 0
            });

            return new ProductManagementPagedResultDto
            {
                Items      = dtos,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            };
        }

        public async Task<ProductDetailDto> UpdateProductAsync(Guid id, UpdateProductDto dto)
        {
            var product = await _unitOfWork.Products.GetByIdIncludingDiscontinuedAsync(id);
            if (product is null)
                throw new KeyNotFoundException("Không tìm thấy sản phẩm.");

            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                throw new Exception("Danh mục không tồn tại.");

            if (await _context.Products.AnyAsync(p => p.Sku == dto.Sku && p.Id != id))
                throw new Exception($"SKU '{dto.Sku}' đã được sử dụng bởi sản phẩm khác.");

            if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                product.ImageUrl = await _cloudinaryService.UploadImageAsync(dto.ImageFile, "products");
            }

            product.Name = dto.Name;
            product.Sku = dto.Sku;
            product.StandardListedPrice = dto.StandardListedPrice;
            product.CategoryId = dto.CategoryId;
            product.Unit = dto.Unit;
            // Chỉ ghi đè khi client thực sự gửi giá trị -> tránh xóa mất du lieu neu client (API call truc tiep,
            // khong qua form CEO) khong gui 2 truong optional nay (da tung gay mat du lieu that tren production).
            if (dto.Description != null) product.Description = dto.Description;
            if (dto.Specifications != null) product.Specifications = dto.Specifications;
            product.IsDiscontinued = dto.IsDiscontinued;

            await _unitOfWork.SaveChangesAsync();

            return new ProductDetailDto
            {
                Id                  = product.Id,
                Name                = product.Name,
                Sku                 = product.Sku,
                StandardListedPrice = product.StandardListedPrice,
                Description         = product.Description,
                Specifications      = product.Specifications,
                ImageUrl            = product.ImageUrl,
                CategoryId          = product.CategoryId,
                CategoryName        = product.Category.Name,
                PhysicalStock       = product.Inventories?.Sum(i => i.OnHandQuantity) ?? 0,
                AvailableStock      = product.Inventories?.Sum(i => i.AvailableQuantity) ?? 0,
                AverageRating       = product.AverageRating,
                ReviewCount         = product.ReviewCount
            };
        }

        public async Task DeleteProductAsync(Guid id)
        {
            var product = await _unitOfWork.Products.GetByIdIncludingDiscontinuedAsync(id);
            if (product is null)
                throw new KeyNotFoundException("Không tìm thấy sản phẩm.");

            product.IsDiscontinued = true;
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<ProductStatsResultDto> GetProductStatsAsync(DateTime from, DateTime to)
        {
            var salesRaw = await _context.OrderItems
                .AsNoTracking()
                .Where(oi => oi.Order.CreatedAt >= from && oi.Order.CreatedAt <= to
                    && !CancelledStatuses.Contains(oi.Order.OrderStatus))
                .GroupBy(oi => oi.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    UnitsSold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.Quantity * x.PriceSnapshot),
                    GrossProfit = g.Sum(x => x.Quantity * (x.PriceSnapshot - x.CostSnapshot)),
                    OrderCount = g.Select(x => x.OrderId).Distinct().Count()
                })
                .ToListAsync();

            var salesByProduct = salesRaw.ToDictionary(x => x.ProductId);

            var activeProducts = await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .Where(p => !p.IsDiscontinued)
                .ToListAsync();

            var activeProductIds = activeProducts.Select(p => p.Id).ToHashSet();
            var productLookup = activeProducts.ToDictionary(p => p.Id);

            var totalProducts = await _context.Products.CountAsync();
            var discontinuedProducts = await _context.Products.CountAsync(p => p.IsDiscontinued);

            var summary = new ProductStatsSummaryDto
            {
                TotalProducts       = totalProducts,
                ActiveProducts      = totalProducts - discontinuedProducts,
                DiscontinuedProducts = discontinuedProducts,
                TotalUnitsSold      = salesRaw.Sum(x => x.UnitsSold),
                TotalRevenue        = salesRaw.Sum(x => x.Revenue),
                TotalGrossProfit    = salesRaw.Sum(x => x.GrossProfit)
            };

            ProductSalesStatDto BuildStat(Guid productId, int unitsSold, decimal revenue, decimal grossProfit, int orderCount)
            {
                productLookup.TryGetValue(productId, out var p);
                return new ProductSalesStatDto
                {
                    ProductId      = productId,
                    Name           = p?.Name ?? "N/A",
                    Sku            = p?.Sku ?? "N/A",
                    ImageUrl       = p?.ImageUrl,
                    CategoryName   = p?.Category.Name ?? "N/A",
                    UnitsSold      = unitsSold,
                    Revenue        = revenue,
                    GrossProfit    = grossProfit,
                    OrderCount     = orderCount,
                    AvailableStock = p?.Inventories?.Sum(i => i.AvailableQuantity) ?? 0
                };
            }

            var bestSellers = salesRaw
                .OrderByDescending(x => x.UnitsSold)
                .Take(TopListLimit)
                .Select(x => BuildStat(x.ProductId, x.UnitsSold, x.Revenue, x.GrossProfit, x.OrderCount))
                .ToList();

            // Sản phẩm bán chậm: chỉ xét sản phẩm đang hoạt động, kể cả sản phẩm chưa bán được lần nào (UnitsSold = 0)
            var slowMovers = activeProductIds
                .Select(pid => salesByProduct.TryGetValue(pid, out var s)
                    ? BuildStat(pid, s.UnitsSold, s.Revenue, s.GrossProfit, s.OrderCount)
                    : BuildStat(pid, 0, 0m, 0m, 0))
                .OrderBy(x => x.UnitsSold)
                .Take(TopListLimit)
                .ToList();

            return new ProductStatsResultDto
            {
                PeriodFrom = from,
                PeriodTo   = to,
                Summary    = summary,
                BestSellers = bestSellers,
                SlowMovers  = slowMovers
            };
        }
    }
}
