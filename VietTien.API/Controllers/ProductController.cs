using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
        private readonly IAuditLogService _auditLogService;

        public ProductController(IProductService productService, IAuditLogService auditLogService)
        {
            _productService = productService;
            _auditLogService = auditLogService;
        }

        private Guid GetUserId()
        {
            var v = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(v) || !Guid.TryParse(v, out var id))
                throw new UnauthorizedAccessException("Invalid user token.");
            return id;
        }

        private string GetUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        private string? GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value;
        private string? GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private async Task<CategoryDto?> FindCategoryAsync(Guid id)
        {
            var categories = await _productService.GetCategoriesForManagementAsync();
            return categories.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>Lấy danh sách sản phẩm (có phân trang, lọc danh mục, khoảng giá, tìm kiếm)</summary>
        /// <param name="page">Trang hiện tại (mặc định: 1)</param>
        /// <param name="pageSize">Số sản phẩm mỗi trang (mặc định: 12, tối đa: 100)</param>
        /// <param name="categoryId">Lọc theo danh mục (tùy chọn)</param>
        /// <param name="search">Từ khóa tìm kiếm theo tên hoặc SKU (tùy chọn)</param>
        /// <param name="sortBy">Sắp xếp: "price-low", "price-high", "sales", "newest" (mặc định: theo tên A-Z)</param>
        /// <param name="minPrice">Giá tối thiểu (tùy chọn)</param>
        /// <param name="maxPrice">Giá tối đa (tùy chọn)</param>
        [HttpGet]
        [ProducesResponseType(typeof(ProductPagedResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] string? search = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] decimal? minPrice = null,
            [FromQuery] decimal? maxPrice = null)
        {
            var result = await _productService.GetProductsAsync(page, pageSize, categoryId, search, sortBy, minPrice, maxPrice);
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

        /// <summary>P2-8: Lấy toàn bộ danh mục (kể cả đã tắt) cho trang quản lý CEO/Admin</summary>
        [HttpGet("categories/management")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(IEnumerable<CategoryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategoriesForManagement()
        {
            var categories = await _productService.GetCategoriesForManagementAsync();
            return Ok(categories);
        }

        /// <summary>P2-8: Tạo danh mục mới</summary>
        [HttpPost("categories")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest dto)
        {
            try
            {
                var result = await _productService.CreateCategoryAsync(dto);

                await _auditLogService.LogAsync(
                    entityName: "ProductCategory", entityId: result.Id.ToString(), action: "CREATE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: null, after: result, ipAddress: GetIp());

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>P2-8: Cập nhật danh mục</summary>
        [HttpPut("categories/{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateCategory([FromRoute] Guid id, [FromBody] UpdateCategoryRequest dto)
        {
            try
            {
                var before = await FindCategoryAsync(id);
                var result = await _productService.UpdateCategoryAsync(id, dto);

                await _auditLogService.LogAsync(
                    entityName: "ProductCategory", entityId: id.ToString(), action: "UPDATE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: before, after: result, ipAddress: GetIp());

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>P2-8: Xóa mềm danh mục (tắt IsActive)</summary>
        [HttpDelete("categories/{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteCategory([FromRoute] Guid id)
        {
            try
            {
                var before = await FindCategoryAsync(id);
                await _productService.DeleteCategoryAsync(id);

                await _auditLogService.LogAsync(
                    entityName: "ProductCategory", entityId: id.ToString(), action: "DELETE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: before, after: null, ipAddress: GetIp());

                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Tạo sản phẩm mới</summary>
        [HttpPost]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateProduct([FromForm] CreateProductDto dto)
        {
            try
            {
                var result = await _productService.CreateProductAsync(dto);

                await _auditLogService.LogAsync(
                    entityName: "Product", entityId: result.Id.ToString(), action: "CREATE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: null, after: result, ipAddress: GetIp());

                return CreatedAtAction(nameof(GetProductById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Lấy danh sách sản phẩm cho trang quản lý (CEO/Admin) - bao gồm cả sản phẩm ngừng kinh doanh</summary>
        /// <param name="page">Trang hiện tại (mặc định: 1)</param>
        /// <param name="pageSize">Số sản phẩm mỗi trang (mặc định: 12, tối đa: 100)</param>
        /// <param name="categoryId">Lọc theo danh mục (tùy chọn)</param>
        /// <param name="search">Từ khóa tìm kiếm theo tên hoặc SKU (tùy chọn)</param>
        /// <param name="isDiscontinued">Lọc theo trạng thái ngừng kinh doanh (tùy chọn, bỏ trống = lấy tất cả)</param>
        [HttpGet("management")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(ProductManagementPagedResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProductsForManagement(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] string? search = null,
            [FromQuery] bool? isDiscontinued = null)
        {
            var result = await _productService.GetProductsForManagementAsync(page, pageSize, categoryId, search, isDiscontinued);
            return Ok(result);
        }

        /// <summary>Thống kê bán hàng theo sản phẩm: tổng quan, top bán chạy, top bán chậm</summary>
        /// <param name="from">Từ ngày (mặc định: 30 ngày trước)</param>
        /// <param name="to">Đến ngày (mặc định: hiện tại)</param>
        [HttpGet("stats")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(ProductStatsResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetProductStats([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var effectiveTo = to ?? DateTime.UtcNow;
            var effectiveFrom = from ?? effectiveTo.AddDays(-30);

            if (effectiveFrom > effectiveTo)
                return BadRequest(new { message = "Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn hoặc bằng 'to'." });

            var result = await _productService.GetProductStatsAsync(effectiveFrom, effectiveTo);
            return Ok(result);
        }

        /// <summary>Cập nhật thông tin sản phẩm</summary>
        /// <param name="id">ID của sản phẩm</param>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateProduct([FromRoute] Guid id, [FromForm] UpdateProductDto dto)
        {
            try
            {
                var before = await _productService.GetProductByIdAsync(id);
                var result = await _productService.UpdateProductAsync(id, dto);

                await _auditLogService.LogAsync(
                    entityName: "Product", entityId: id.ToString(), action: "UPDATE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: before, after: result, ipAddress: GetIp());

                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Sản phẩm đã bị thay đổi bởi người khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Xóa (ngừng kinh doanh) sản phẩm</summary>
        /// <param name="id">ID của sản phẩm</param>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "CEO,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteProduct([FromRoute] Guid id)
        {
            try
            {
                var before = await _productService.GetProductByIdAsync(id);
                await _productService.DeleteProductAsync(id);

                await _auditLogService.LogAsync(
                    entityName: "Product", entityId: id.ToString(), action: "DELETE",
                    actorUserId: GetUserId(), actorEmail: GetUserEmail(), actorRole: GetUserRole(),
                    before: before, after: null, ipAddress: GetIp());

                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Sản phẩm đã bị thay đổi bởi người khác. Vui lòng tải lại và thử lại." });
            }
        }
    }
}
