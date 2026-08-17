using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VietTien.API.DTOs.ProductPriceUpdate;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // Luồng cập nhật giá hàng hóa: chỉ CEO/SalesManager/SalesStaff/Admin thao tác — khách hàng chỉ
    // nhận thông báo (SYS_46_ProductPriceUpdateScheduleNotice), không có quyền truy cập endpoint nào ở đây.
    [Route("api/product-price-update-orders")]
    [ApiController]
    [Authorize(Roles = "CEO,SalesManager,SalesStaff,Admin")]
    public class ProductPriceUpdateOrderController : ControllerBase
    {
        private readonly IProductPriceUpdateService _service;

        public ProductPriceUpdateOrderController(IProductPriceUpdateService service)
        {
            _service = service;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string GetUserRole() => User.FindFirstValue(ClaimTypes.Role)!;

        [HttpPost]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> Propose([FromBody] CreateProductPriceUpdateOrderRequest request)
        {
            try
            {
                return Ok(await _service.ProposeAsync(GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            try
            {
                var role = GetUserRole();
                if (role == "SalesManager")
                    return Ok(await _service.GetPendingForManagerAsync());
                if (role == "SalesStaff")
                    return Ok(await _service.GetPendingForStaffAsync(GetUserId()));

                // CEO/Admin: xem toàn bộ lịch sử để theo dõi tuân thủ.
                return Ok(await _service.GetAllAsync());
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                return Ok(await _service.GetByIdAsync(id));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/assign")]
        [Authorize(Roles = "SalesManager")]
        public async Task<IActionResult> AssignAndNotify(Guid id, [FromBody] AssignPriceUpdateOrderRequest request)
        {
            try
            {
                return Ok(await _service.AssignAndNotifyAsync(id, GetUserId(), request));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Đợt cập nhật giá đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/execute")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> Execute(Guid id)
        {
            try
            {
                return Ok(await _service.ExecuteAsync(id, GetUserId()));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Đợt cập nhật giá đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "CEO,SalesManager")]
        public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelPriceUpdateOrderRequest request)
        {
            try
            {
                return Ok(await _service.CancelAsync(id, GetUserId(), GetUserRole(), request));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Đợt cập nhật giá đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
