using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Product;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/products")]
    [Produces("application/json")]
    public class ProductController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductController(IProductService productService)
        {
            _productService = productService;
        }

        /// <summary>Lấy danh sách sản phẩm (có phân trang, lọc danh mục, tìm kiếm)</summary>
        /// <param name="page">Trang hiện tại (mặc định: 1)</param>
        /// <param name="pageSize">Số sản phẩm mỗi trang (mặc định: 12, tối đa: 100)</param>
        /// <param name="categoryId">Lọc theo danh mục (tùy chọn)</param>
        /// <param name="search">Từ khóa tìm kiếm theo tên hoặc SKU (tùy chọn)</param>
        [HttpGet]
        [ProducesResponseType(typeof(ProductPagedResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] string? search = null,
            [FromQuery] string? sortBy = null)
        {
            var result = await _productService.GetProductsAsync(page, pageSize, categoryId, search, sortBy);
            return Ok(result);
        }

        /// <summary>Lấy chi tiết 1 sản phẩm theo ID</summary>
        /// <param name="id">ID của sản phẩm</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetProductById([FromRoute] Guid id)
        {
            var product = await _productService.GetProductByIdAsync(id);

            if (product is null)
                return NotFound(new { message = "Không tìm thấy sản phẩm." });

            return Ok(product);
        }

        /// <summary>Lấy danh sách tất cả danh mục đang hoạt động</summary>
        [HttpGet("categories")]
        [ProducesResponseType(typeof(IEnumerable<CategoryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _productService.GetCategoriesAsync();
            return Ok(categories);
        }

        /// <summary>Tạo sản phẩm mới</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateProduct([FromForm] CreateProductDto dto)
        {
            try
            {
                var result = await _productService.CreateProductAsync(dto);
                return CreatedAtAction(nameof(GetProductById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
