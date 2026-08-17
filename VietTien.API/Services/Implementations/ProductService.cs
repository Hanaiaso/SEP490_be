using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Product;
using VietTien.API.Infrastructure.Validation;
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
        private const int MaxImagesPerProduct = 8;

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

            return BuildProductDetailDto(product);
        }

        private static ProductDetailDto BuildProductDetailDto(VietTien.API.Models.Product product)
        {
            var orderedImages = product.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => new ProductImageDto { Id = i.Id, ImageUrl = i.ImageUrl, SortOrder = i.SortOrder })
                .ToList();

            return new ProductDetailDto
            {
                Id                  = product.Id,
                Name                = product.Name,
                Sku                 = product.Sku,
                StandardListedPrice = product.StandardListedPrice,
                Description         = product.Description,
                Specifications      = product.Specifications,
                ImageUrl            = product.ImageUrl,
                Images              = orderedImages,
                CategoryId          = product.CategoryId,
                CategoryName        = product.Category.Name,
                PhysicalStock       = product.Inventories?.Sum(i => i.OnHandQuantity) ?? 0,
                AvailableStock      = product.Inventories?.Sum(i => i.AvailableQuantity) ?? 0,
                AverageRating       = product.AverageRating,
                ReviewCount         = product.ReviewCount
            };
        }

        /// <summary>Validate + upload lần lượt các file ảnh mới lên Cloudinary, trả về URL theo đúng thứ tự upload</summary>
        private async Task<List<string>> UploadProductImagesAsync(IEnumerable<IFormFile> files)
        {
            var urls = new List<string>();
            foreach (var file in files)
            {
                if (file is null || file.Length == 0) continue;

                var error = ImageFileAttribute.Validate(file, 5);
                if (error != null)
                    throw new ArgumentException(error);

                urls.Add(await _cloudinaryService.UploadImageAsync(file, "products"));
            }
            return urls;
        }

        public async Task<IEnumerable<CategoryDto>> GetCategoriesAsync()
        {
            var categories = await _unitOfWork.Products.GetActiveCategoriesAsync();

            return categories.Select(c => new CategoryDto
            {
                Id          = c.Id,
                Name        = c.Name,
                Description = c.Description,
                IsActive    = c.IsActive
            });
        }

        // P2-8: trang quản lý CEO/Admin cần thấy cả danh mục đã tắt để có thể bật lại.
        public async Task<IEnumerable<CategoryDto>> GetCategoriesForManagementAsync()
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            return categories.Select(c => new CategoryDto
            {
                Id          = c.Id,
                Name        = c.Name,
                Description = c.Description,
                IsActive    = c.IsActive
            });
        }

        public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest dto)
        {
            var name = dto.Name.Trim();
            if (await _context.Categories.AnyAsync(c => c.Name == name))
                throw new InvalidOperationException($"Danh mục '{name}' đã tồn tại.");

            var category = new Category
            {
                Name = name,
                Description = dto.Description?.Trim(),
                IsActive = true
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return new CategoryDto { Id = category.Id, Name = category.Name, Description = category.Description, IsActive = category.IsActive };
        }

        public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest dto)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new KeyNotFoundException("Không tìm thấy danh mục.");

            var name = dto.Name.Trim();
            if (await _context.Categories.AnyAsync(c => c.Id != id && c.Name == name))
                throw new InvalidOperationException($"Danh mục '{name}' đã tồn tại.");

            category.Name = name;
            category.Description = dto.Description?.Trim();
            category.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return new CategoryDto { Id = category.Id, Name = category.Name, Description = category.Description, IsActive = category.IsActive };
        }

        public async Task DeleteCategoryAsync(Guid id)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new KeyNotFoundException("Không tìm thấy danh mục.");

            category.IsActive = false;
            await _context.SaveChangesAsync();
        }

        public async Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                throw new Exception("Danh mục không tồn tại.");

            if (await _context.Products.AnyAsync(p => p.Sku == dto.Sku))
                throw new Exception($"SKU '{dto.Sku}' đã được sử dụng bởi sản phẩm khác.");

            var files = dto.ImageFiles?.Where(f => f != null && f.Length > 0).ToList() ?? new List<IFormFile>();
            if (files.Count > MaxImagesPerProduct)
                throw new ArgumentException($"Chỉ được tải tối đa {MaxImagesPerProduct} ảnh cho mỗi sản phẩm.");

            var imageUrls = await UploadProductImagesAsync(files);

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
                ImageUrl = imageUrls.FirstOrDefault(),
                IsDiscontinued = false,
                ReorderThreshold = dto.ReorderThreshold,
                ExcessThreshold = dto.ExcessThreshold
            };

            for (var i = 0; i < imageUrls.Count; i++)
            {
                newProduct.Images.Add(new ProductImage
                {
                    ProductId = newProduct.Id,
                    ImageUrl = imageUrls[i],
                    SortOrder = i
                });
            }

            await _unitOfWork.Products.AddAsync(newProduct);

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Lưới an toàn cuối cho race check-then-insert ở AnyAsync(Sku) phía trên: 2 request đồng
                // thời cùng tạo sản phẩm với SKU trùng nhau đều pass check rồi mới đụng unique index lúc
                // SaveChanges -> request thua cuộc nhận lỗi rõ ràng thay vì 500 chung chung.
                throw new InvalidOperationException($"SKU '{dto.Sku}' đã được sử dụng bởi sản phẩm khác.");
            }

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
                ReorderThreshold    = p.ReorderThreshold,
                ExcessThreshold     = p.ExcessThreshold,
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

            // Xóa các ảnh được chọn (nếu có) — dọn luôn trên Cloudinary theo đúng pattern UserProfileService.DeleteAvatarAsync
            if (dto.RemoveImageIds != null && dto.RemoveImageIds.Count > 0)
            {
                var toRemove = product.Images.Where(img => dto.RemoveImageIds.Contains(img.Id)).ToList();
                foreach (var img in toRemove)
                {
                    var publicId = _cloudinaryService.ExtractPublicId(img.ImageUrl);
                    if (publicId != null)
                        await _cloudinaryService.DeleteImageAsync(publicId);

                    product.Images.Remove(img);
                    _context.ProductImages.Remove(img);
                }
            }

            var newFiles = dto.ImageFiles?.Where(f => f != null && f.Length > 0).ToList() ?? new List<IFormFile>();
            if (product.Images.Count + newFiles.Count > MaxImagesPerProduct)
                throw new ArgumentException($"Chỉ được tải tối đa {MaxImagesPerProduct} ảnh cho mỗi sản phẩm.");

            var newImageUrls = await UploadProductImagesAsync(newFiles);
            var nextSortOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1;
            for (var i = 0; i < newImageUrls.Count; i++)
            {
                var newImage = new ProductImage
                {
                    ProductId = product.Id,
                    ImageUrl = newImageUrls[i],
                    SortOrder = nextSortOrder + i
                };
                product.Images.Add(newImage);
                // Id được set sẵn ở model (Guid.NewGuid()) nên EF không tự nhận ra đây là entity mới khi
                // thêm qua navigation collection của 1 entity đã tracked -> phải ép state Added tường minh,
                // nếu không EF sinh nhầm UPDATE cho 1 dòng chưa tồn tại (0 rows affected -> DbUpdateConcurrencyException giả).
                // Xem pattern tương tự: WarehouseManagementService.cs (newLoc).
                _context.Entry(newImage).State = EntityState.Added;
            }

            // ImageUrl giữ vai trò ảnh đại diện — luôn đồng bộ với ảnh đầu tiên (theo SortOrder) của gallery
            product.ImageUrl = product.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.ImageUrl;

            product.Name = dto.Name;
            product.Sku = dto.Sku;
            product.StandardListedPrice = dto.StandardListedPrice;
            product.CategoryId = dto.CategoryId;
            product.Unit = dto.Unit;
            product.ReorderThreshold = dto.ReorderThreshold;
            product.ExcessThreshold = dto.ExcessThreshold;
            // Chỉ ghi đè khi client thực sự gửi giá trị -> tránh xóa mất du lieu neu client (API call truc tiep,
            // khong qua form CEO) khong gui 2 truong optional nay (da tung gay mat du lieu that tren production).
            if (dto.Description != null) product.Description = dto.Description;
            if (dto.Specifications != null) product.Specifications = dto.Specifications;
            product.IsDiscontinued = dto.IsDiscontinued;

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new InvalidOperationException($"SKU '{dto.Sku}' đã được sử dụng bởi sản phẩm khác.");
            }

            return BuildProductDetailDto(product);
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

        // SQL Server error 2601 (unique index) / 2627 (unique constraint) — dùng để phân biệt vi phạm
        // unique index (Product.Sku) khỏi các lỗi DbUpdateException khác không nên bị nuốt thành 409.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}
