using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Material;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/materials")]
    [Produces("application/json")]
    [Authorize]
    public class MaterialController : ControllerBase
    {
        private readonly IMaterialService _materialService;
        private readonly IGoodsIssueService _goodsIssueService;

        public MaterialController(IMaterialService materialService, IGoodsIssueService goodsIssueService)
        {
            _materialService = materialService;
            _goodsIssueService = goodsIssueService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<MaterialDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] string? search = null)
        {
            var materials = await _materialService.GetAllAsync(search);
            return Ok(materials);
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var material = await _materialService.GetByIdAsync(id);
                return Ok(material);
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

        [HttpPost]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] CreateMaterialDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var material = await _materialService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = material.Id }, material);
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

        [HttpPut("{id}")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        [ProducesResponseType(typeof(MaterialDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMaterialDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var material = await _materialService.UpdateAsync(id, dto);
                return Ok(material);
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

        [HttpDelete("{id}")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _materialService.DeleteAsync(id);
                return NoContent();
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

        // L3-INV-05: gộp Create -> UploadProof -> Post của GoodsIssue (Type=ProductionMaterial) thành
        // 1 lần gọi. Toàn bộ validate bằng chứng (ảnh, người nhận, bộ phận, số biên bản) và trừ tồn
        // kho có kiểm tra invariant đã nằm sẵn trong GoodsIssueService — endpoint này chỉ compose lại.
        [HttpPost("production-issues")]
        [Authorize(Roles = "WarehouseStaff,CEO,Admin")]
        public async Task<IActionResult> CreateProductionIssue(
            [FromForm] CreateProductionIssueRequestDto request, IFormFile? evidencePhoto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (evidencePhoto == null || evidencePhoto.Length == 0)
                return UnprocessableEntity(new { message = "Bắt buộc đính kèm ảnh biên bản giấy đã có chữ ký (evidencePhoto)." });

            var staffIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(staffIdStr)) return Unauthorized();
            var staffId = Guid.Parse(staffIdStr);

            try
            {
                var created = await _goodsIssueService.CreateGoodsIssueAsync(new CreateGoodsIssueRequestDto
                {
                    Type = "ProductionMaterial",
                    WarehouseId = request.WarehouseId,
                    ExternalRecipientName = request.ExternalRecipientName,
                    Department = request.Department,
                    ReceivedAt = request.ReceivedAt,
                    PaperDocumentNumber = request.PaperDocumentNumber,
                    UsagePurpose = request.UsagePurpose,
                    Items = request.Items.Select(i => new CreateGoodsIssueItemRequestDto
                    {
                        ProductId = i.ProductId,
                        MaterialId = i.MaterialId,
                        Quantity = i.Quantity
                    }).ToList()
                }, staffId);

                await _goodsIssueService.UploadProofAsync(created.Id, evidencePhoto);
                var posted = await _goodsIssueService.PostGoodsIssueAsync(created.Id, staffId);

                return Ok(posted);
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
    }
}
