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

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager")]
        public async Task<IActionResult> GetGoodsIssues([FromQuery] string? type)
        {
            try
            {
                var result = await _goodsIssueService.GetGoodsIssuesAsync(type);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager")]
        public async Task<IActionResult> GetGoodsIssueById(Guid id)
        {
            try
            {
                var result = await _goodsIssueService.GetGoodsIssueByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
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

                var result = await _goodsIssueService.UploadProofAsync(id, file);
                return Ok(result);
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
