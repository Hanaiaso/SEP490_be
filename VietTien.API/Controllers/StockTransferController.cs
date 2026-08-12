using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/stock-transfers")]
    [Authorize]
    public class StockTransferController : ControllerBase
    {
        private readonly IStockTransferService _stockTransferService;

        public StockTransferController(IStockTransferService stockTransferService)
        {
            _stockTransferService = stockTransferService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> GetAll([FromQuery] string? status = null)
        {
            try
            {
                var transfers = await _stockTransferService.GetAllAsync(status);
                return Ok(transfers);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var transfer = await _stockTransferService.GetByIdAsync(id);
                return Ok(transfer);
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
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> Create([FromBody] CreateStockTransferDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var transfer = await _stockTransferService.CreateAsync(dto, userId);
                return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, transfer);
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

        [HttpPut("{id}")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStockTransferDto dto)
        {
            try
            {
                var transfer = await _stockTransferService.UpdateAsync(id, dto);
                return Ok(transfer);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Phiếu điều chuyển đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/dispatch")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> DispatchTransfer(Guid id)
        {
            try
            {
                var transfer = await _stockTransferService.DispatchAsync(id);
                return Ok(transfer);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/receive")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> ReceiveTransfer(Guid id, [FromForm] ReceiveStockTransferDto dto)
        {
            try
            {
                var transfer = await _stockTransferService.ReceiveAsync(id, dto, GetUserId());
                return Ok(transfer);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Tồn kho đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> CancelTransfer(Guid id)
        {
            try
            {
                var transfer = await _stockTransferService.CancelAsync(id);
                return Ok(transfer);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Phiếu điều chuyển đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/request-transport")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> RequestTransport(Guid id)
        {
            try
            {
                var transfer = await _stockTransferService.RequestTransportAsync(id);
                return Ok(transfer);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "Phiếu điều chuyển đã bị thay đổi bởi tác vụ khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
