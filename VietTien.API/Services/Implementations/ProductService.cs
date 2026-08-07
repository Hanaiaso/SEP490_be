using VietTien.API.DTOs.Product;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class ProductService : IProductService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICloudinaryService _cloudinaryService;

        public ProductService(IUnitOfWork unitOfWork, ICloudinaryService cloudinaryService)
        {
            _unitOfWork = unitOfWork;
            _cloudinaryService = cloudinaryService;
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
    }
}
