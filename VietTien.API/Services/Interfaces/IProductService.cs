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
            string? sortBy = null,
            decimal? minPrice = null,
            decimal? maxPrice = null);

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
        /// P2-8: Lấy TOÀN BỘ danh mục (kể cả đã tắt) cho trang quản lý CEO/Admin.
        /// </summary>
        Task<IEnumerable<CategoryDto>> GetCategoriesForManagementAsync();

        /// <summary>P2-8: Tạo danh mục mới.</summary>
        Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest dto);

        /// <summary>P2-8: Cập nhật danh mục. Ném KeyNotFoundException nếu không tìm thấy.</summary>
        Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest dto);

        /// <summary>P2-8: Xóa mềm danh mục (IsActive=false). Ném KeyNotFoundException nếu không tìm thấy.</summary>
        Task DeleteCategoryAsync(Guid id);

        /// <summary>
        /// Tạo sản phẩm mới
        /// </summary>
        Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto);

        /// <summary>
        /// Lấy danh sách sản phẩm cho trang quản lý (CEO/Admin), bao gồm số lượng đã bán all-time.
        /// </summary>
        Task<ProductManagementPagedResultDto> GetProductsForManagementAsync(
            int page,
            int pageSize,
            Guid? categoryId = null,
            string? searchKeyword = null,
            bool? isDiscontinued = null);

        /// <summary>
        /// Cập nhật sản phẩm. Ném KeyNotFoundException nếu không tìm thấy.
        /// </summary>
        Task<ProductDetailDto> UpdateProductAsync(Guid id, UpdateProductDto dto);

        /// <summary>
        /// Xóa mềm sản phẩm (đánh dấu ngừng kinh doanh). Ném KeyNotFoundException nếu không tìm thấy.
        /// </summary>
        Task DeleteProductAsync(Guid id);

        /// <summary>
        /// Thống kê bán hàng theo sản phẩm trong khoảng thời gian: tổng quan, top bán chạy, top bán chậm.
        /// </summary>
        Task<ProductStatsResultDto> GetProductStatsAsync(DateTime from, DateTime to);
    }
}
