using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface IProductRepository
    {
        /// <summary>Lấy danh sách sản phẩm (có phân trang, lọc theo danh mục, tìm kiếm)</summary>
        Task<(IEnumerable<Product> Items, int TotalCount)> GetAllAsync(
            int page,
            int pageSize,
            Guid? categoryId = null,
            string? searchKeyword = null,
            string? sortBy = null);

        /// <summary>Lấy chi tiết 1 sản phẩm theo ID (bao gồm Inventory và Category)</summary>
        Task<Product?> GetByIdAsync(Guid id);

        /// <summary>Lấy danh sách tất cả danh mục đang hoạt động</summary>
        Task<IEnumerable<Category>> GetActiveCategoriesAsync();

        /// <summary>Thêm sản phẩm mới</summary>
        Task AddAsync(Product product);
    }
}
