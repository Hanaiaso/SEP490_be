using VietTien.API.DTOs.Product;

namespace VietTien.API.Services.Interfaces
{
    public interface IProductService
    {
        /// <summary>
        /// Lấy danh sách sản phẩm với phân trang, lọc theo danh mục và tìm kiếm theo tên/SKU.
        /// </summary>
        Task<ProductPagedResultDto> GetProductsAsync(
            int page = 1,
            int pageSize = 12,
            Guid? categoryId = null,
            string? searchKeyword = null,
            string? sortBy = null);

        /// <summary>
        /// Lấy chi tiết 1 sản phẩm theo ID.
        /// Trả về null nếu không tìm thấy hoặc sản phẩm đã ngừng kinh doanh.
        /// </summary>
        Task<ProductDetailDto?> GetProductByIdAsync(Guid id);

        /// <summary>
        /// Lấy danh sách tất cả danh mục đang hoạt động.
        /// </summary>
        Task<IEnumerable<CategoryDto>> GetCategoriesAsync();

        /// <summary>
        /// Tạo sản phẩm mới
        /// </summary>
        Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto);
    }
}
