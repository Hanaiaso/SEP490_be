using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using VietTien.API.Infrastructure.Validation;

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

        [ImageFile(5)]
        public IFormFile? ImageFile { get; set; }
    }
}
