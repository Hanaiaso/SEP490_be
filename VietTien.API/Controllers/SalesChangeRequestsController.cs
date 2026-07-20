using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.SalesChange;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    // LUỒNG 7: Khách hàng yêu cầu đổi nhân viên Sale phụ trách (WF-07; CUS-11, MGR-06)
    [Route("api/sales-change-requests")]
    [ApiController]
    [Authorize]
    public class SalesChangeRequestsController : ControllerBase
    {
        private readonly ISalesChangeRequestService _service;

        public SalesChangeRequestsController(ISalesChangeRequestService service)
        {
            _service = service;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ─── CUSTOMER ─────────────────────────────────────────────────────────────

        /// <summary>Bước 1: khách tạo yêu cầu đổi Sale (multipart, kèm ảnh bằng chứng)</summary>
        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Create([FromForm] CreateSalesChangeRequestDto dto)
        {
            try
            {
                var id = await _service.CreateAsync(GetUserId(), dto);
                return Ok(new { id, message = "Đã gửi yêu cầu đổi Sale. Quản lý sẽ xử lý trong thời gian sớm nhất." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("mine")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetMine()
        {
            try { return Ok(await _service.GetMineAsync(GetUserId())); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpGet("my-assigned-sale")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetMyAssignedSale()
        {
            try { return Ok(await _service.GetMyAssignedSaleAsync(GetUserId())); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpGet("sales-options")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetSalesOptions()
            => Ok(await _service.GetSalesOptionsAsync());

        [HttpPut("{id}/cancel")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            try
            {
                await _service.CancelAsync(GetUserId(), id);
                return Ok(new { message = "Đã hủy yêu cầu." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("{id}/additional-info")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> SubmitAdditionalInfo(Guid id, [FromBody] AdditionalInfoDto dto)
        {
            try
            {
                await _service.SubmitAdditionalInfoAsync(GetUserId(), id, dto.Info);
                return Ok(new { message = "Đã gửi thông tin bổ sung." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // ─── SALES STAFF (Sale hiện tại) ──────────────────────────────────────────
        // Bước 3: Sale chỉ xem + giải trình; cố tình KHÔNG có endpoint sửa/xóa/từ chối cho Sale.
        // Gate bảo vệ khách: chỉ trả về yêu cầu đã được Manager bấm "Yêu cầu giải trình".

        [HttpGet("about-me")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> GetAboutMe()
            => Ok(await _service.GetAboutMeAsync(GetUserId()));

        /// <summary>Giải trình của Sale (multipart, kèm file đính kèm)</summary>
        [HttpPut("{id}/explanation")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> SubmitExplanation(Guid id, [FromForm] SaleExplanationDto dto)
        {
            try
            {
                await _service.SubmitExplanationAsync(GetUserId(), id, dto);
                return Ok(new { message = "Đã gửi giải trình." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // ─── SALES MANAGER ────────────────────────────────────────────────────────

        [HttpGet]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetPaged([FromQuery] SalesChangeRequestQueryDto query)
            => Ok(await _service.GetPagedAsync(query));

        [HttpGet("{id}")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetDetail(Guid id)
        {
            try { return Ok(await _service.GetDetailAsync(id)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpGet("{id}/review-context")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> GetReviewContext(Guid id)
        {
            try { return Ok(await _service.GetReviewContextAsync(id)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        /// <summary>Gate bảo vệ khách: Manager mở giải trình thì Sale hiện tại mới thấy được khiếu nại</summary>
        [HttpPut("{id}/request-explanation")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> RequestExplanation(Guid id)
        {
            try
            {
                await _service.RequestExplanationAsync(GetUserId(), id);
                return Ok(new { message = "Đã yêu cầu Sale giải trình." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("{id}/request-info")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> RequestMoreInfo(Guid id, [FromBody] ManagerNoteDto dto)
        {
            try
            {
                await _service.RequestMoreInfoAsync(GetUserId(), id, dto.Note);
                return Ok(new { message = "Đã yêu cầu khách bổ sung thông tin." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("{id}/reject")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> Reject(Guid id, [FromBody] ManagerNoteDto dto)
        {
            try
            {
                await _service.RejectAsync(GetUserId(), id, dto.Note);
                return Ok(new { message = "Đã từ chối yêu cầu." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPut("{id}/approve")]
        [Authorize(Roles = "SalesManager,Admin")]
        public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveSalesChangeRequestDto dto)
        {
            try
            {
                await _service.ApproveAsync(GetUserId(), id, dto);
                return Ok(new { message = "Đã phê duyệt yêu cầu và chuyển Sale phụ trách." });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }
    }
}
