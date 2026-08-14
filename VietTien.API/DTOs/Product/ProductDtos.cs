using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace VietTien.API.DTOs.Product
{
    /// <summary>DTO trả về khi lấy danh sách sản phẩm (dạng card/list)</summary>
    public class ProductSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal StandardListedPrice { get; set; }
        public string? ImageUrl { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }

        /// <summary>Tồn kho khả dụng (null nếu chưa có dữ liệu kho)</summary>
        public int? AvailableStock { get; set; }

        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    /// <summary>1 ảnh trong gallery sản phẩm</summary>
    public class ProductImageDto
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    /// <summary>DTO trả về khi lấy chi tiết 1 sản phẩm</summary>
    public class ProductDetailDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal StandardListedPrice { get; set; }
        public string? Description { get; set; }
        public string? Specifications { get; set; }

        /// <summary>Ảnh đại diện (trùng với ảnh đầu tiên trong Images) — giữ lại để tương thích ngược</summary>
        public string? ImageUrl { get; set; }

        /// <summary>Toàn bộ ảnh sản phẩm theo thứ tự hiển thị</summary>
        public List<ProductImageDto> Images { get; set; } = new();
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>Tồn kho thực tế (null nếu chưa có dữ liệu kho)</summary>
        public int? PhysicalStock { get; set; }

        /// <summary>Tồn kho khả dụng cho kênh online (null nếu chưa có dữ liệu kho)</summary>
        public int? AvailableStock { get; set; }

        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    /// <summary>DTO trả về danh sách sản phẩm có phân trang</summary>
    public class ProductPagedResultDto
    {
        public IEnumerable<ProductSummaryDto> Items { get; set; } = new List<ProductSummaryDto>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    /// <summary>DTO trả về thông tin danh mục</summary>
    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        // P2-8: trạng thái danh mục (Admin/CEO quản lý)
        public bool IsActive { get; set; }
    }

    public class CreateCategoryRequest
    {
        [Required(ErrorMessage = "Tên danh mục là bắt buộc.")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }
    }

    public class UpdateCategoryRequest
    {
        [Required(ErrorMessage = "Tên danh mục là bắt buộc.")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class CreateProductDto
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
        [MaxLength(300, ErrorMessage = "Tên sản phẩm không được vượt quá 300 ký tự.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "SKU là bắt buộc.")]
        [MaxLength(50, ErrorMessage = "SKU không được vượt quá 50 ký tự.")]
        public string Sku { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue, ErrorMessage = "Giá niêm yết phải lớn hơn 0")]
        public decimal StandardListedPrice { get; set; }

        [Required(ErrorMessage = "Danh mục là bắt buộc.")]
        public Guid CategoryId { get; set; }

        [Required(ErrorMessage = "Đơn vị tính là bắt buộc.")]
        [MaxLength(50, ErrorMessage = "Đơn vị tính không được vượt quá 50 ký tự.")]
        public string Unit { get; set; } = "Cái";

        public string? Description { get; set; }

        public string? Specifications { get; set; }

        /// <summary>Danh sách ảnh sản phẩm (ảnh đầu tiên trở thành ảnh đại diện). Từng file được validate thủ công trong ProductService.</summary>
        public List<IFormFile>? ImageFiles { get; set; }
    }

    /// <summary>DTO cập nhật sản phẩm (dùng bởi CEO/Admin)</summary>
    public class UpdateProductDto
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
        [MaxLength(300, ErrorMessage = "Tên sản phẩm không được vượt quá 300 ký tự.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "SKU là bắt buộc.")]
        [MaxLength(50, ErrorMessage = "SKU không được vượt quá 50 ký tự.")]
        public string Sku { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue, ErrorMessage = "Giá niêm yết phải lớn hơn 0")]
        public decimal StandardListedPrice { get; set; }

        [Required(ErrorMessage = "Danh mục là bắt buộc.")]
        public Guid CategoryId { get; set; }

        [Required(ErrorMessage = "Đơn vị tính là bắt buộc.")]
        [MaxLength(50, ErrorMessage = "Đơn vị tính không được vượt quá 50 ký tự.")]
        public string Unit { get; set; } = "Cái";

        public string? Description { get; set; }

        public string? Specifications { get; set; }

        /// <summary>Ảnh mới cần thêm vào gallery (giữ nguyên ảnh cũ trừ khi liệt kê trong RemoveImageIds). Từng file được validate thủ công trong ProductService.</summary>
        public List<IFormFile>? ImageFiles { get; set; }

        /// <summary>Id các ProductImage cần xóa khỏi gallery</summary>
        public List<Guid>? RemoveImageIds { get; set; }

        /// <summary>Ngừng kinh doanh sản phẩm (true) hoặc khôi phục lại (false)</summary>
        public bool IsDiscontinued { get; set; } = false;
    }

    /// <summary>DTO trả về 1 dòng trong bảng quản lý sản phẩm (CEO/Admin)</summary>
    public class ProductManagementItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal StandardListedPrice { get; set; }
        public string? ImageUrl { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        /// <summary>Tồn kho khả dụng (null nếu chưa có dữ liệu kho)</summary>
        public int? AvailableStock { get; set; }

        public bool IsDiscontinued { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }

        /// <summary>Tổng số lượng đã bán (all-time, không tính đơn đã hủy)</summary>
        public int UnitsSoldTotal { get; set; }
    }

    /// <summary>DTO trả về danh sách sản phẩm có phân trang cho trang quản lý</summary>
    public class ProductManagementPagedResultDto
    {
        public IEnumerable<ProductManagementItemDto> Items { get; set; } = new List<ProductManagementItemDto>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    /// <summary>Số liệu tổng quan sản phẩm trong 1 khoảng thời gian</summary>
    public class ProductStatsSummaryDto
    {
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int DiscontinuedProducts { get; set; }
        public int TotalUnitsSold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalGrossProfit { get; set; }
    }

    /// <summary>Số liệu bán hàng của 1 sản phẩm trong 1 khoảng thời gian</summary>
    public class ProductSalesStatDto
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int UnitsSold { get; set; }
        public decimal Revenue { get; set; }
        public decimal GrossProfit { get; set; }
        public int OrderCount { get; set; }
        public int? AvailableStock { get; set; }
    }

    /// <summary>Kết quả thống kê sản phẩm trả về cho trang quản lý CEO/Admin</summary>
    public class ProductStatsResultDto
    {
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public ProductStatsSummaryDto Summary { get; set; } = new();
        public List<ProductSalesStatDto> BestSellers { get; set; } = new();
        public List<ProductSalesStatDto> SlowMovers { get; set; } = new();
    }
}
