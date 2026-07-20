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
        public string? ImageUrl { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>Tồn kho thực tế (null nếu chưa có dữ liệu kho)</summary>
        public int? PhysicalStock { get; set; }

        /// <summary>Tồn kho khả dụng cho kênh online (null nếu chưa có dữ liệu kho)</summary>
        public int? AvailableStock { get; set; }
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
    }

    public class CreateProductDto
    {
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal StandardListedPrice { get; set; }
        public Guid CategoryId { get; set; }
        public string Unit { get; set; } = "Cái";
        public string? Description { get; set; }
        public string? Specifications { get; set; }
        public IFormFile? ImageFile { get; set; }
    }
}
