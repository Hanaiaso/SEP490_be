using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/goods-issues")]
    [ApiController]
    [Authorize]
    public class GoodsIssueController : ControllerBase
    {
        private readonly IGoodsIssueService _goodsIssueService;

        public GoodsIssueController(IGoodsIssueService goodsIssueService)
        {
            _goodsIssueService = goodsIssueService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return userId;
        }

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> GetGoodsIssues([FromQuery] string? type)
        {
            try
            {
                var result = await _goodsIssueService.GetGoodsIssuesAsync(type, GetUserId());
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> GetGoodsIssueById(Guid id)
        {
            try
            {
                var result = await _goodsIssueService.GetGoodsIssueByIdAsync(id, GetUserId());
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}/export-excel")]
        [Authorize(Roles = "WarehouseStaff,SalesStaff,SalesManager,CEO,Admin")]
        public async Task<IActionResult> ExportExcel(Guid id)
        {
            try
            {
                var bytes = await _goodsIssueService.ExportExcelAsync(id, GetUserId());
                var fileName = $"phieu-xuat-kho-{id}.xlsx";
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> CreateGoodsIssue([FromBody] CreateGoodsIssueRequestDto request)
        {
            try
            {
                var staffIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(staffIdStr)) return Unauthorized();

                var result = await _goodsIssueService.CreateGoodsIssueAsync(request, Guid.Parse(staffIdStr));
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/upload-proof")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> UploadProof(Guid id, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { message = "Vui lòng chọn file chứng từ." });
                }

                var result = await _goodsIssueService.UploadProofAsync(id, GetUserId(), file);
                return Ok(result);
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
                return Conflict(new { message = "Phiếu xuất kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}/handover")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> UpdateHandoverInfo(Guid id, [FromBody] UpdateGoodsIssueHandoverDto dto)
        {
            try
            {
                var result = await _goodsIssueService.UpdateHandoverInfoAsync(id, GetUserId(), dto);
                return Ok(result);
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
                return Conflict(new { message = "Phiếu xuất kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/post")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> PostGoodsIssue(Guid id)
        {
            try
            {
                var staffIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(staffIdStr)) return Unauthorized();

                var result = await _goodsIssueService.PostGoodsIssueAsync(id, Guid.Parse(staffIdStr));
                return Ok(result);
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
                return Conflict(new { message = "Tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/reversal")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> CreateReversal(Guid id, [FromBody] CreateReversalRequestDto dto)
        {
            try
            {
                var staffIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(staffIdStr)) return Unauthorized();

                var result = await _goodsIssueService.CreateReversalAsync(id, dto, Guid.Parse(staffIdStr));
                return Ok(result);
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
                return Conflict(new { message = "Tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
