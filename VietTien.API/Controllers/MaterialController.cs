using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Material;
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

        public MaterialController(IMaterialService materialService)
        {
            _materialService = materialService;
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
    }
}
