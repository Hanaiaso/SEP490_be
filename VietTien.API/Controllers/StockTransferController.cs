using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        [HttpGet]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> GetAll()
        {
            var transfers = await _stockTransferService.GetAllAsync();
            return Ok(transfers);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var transfer = await _stockTransferService.GetByIdAsync(id);
            return Ok(transfer);
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

            var transfer = await _stockTransferService.CreateAsync(dto, userId);
            return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, transfer);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStockTransferDto dto)
        {
            var transfer = await _stockTransferService.UpdateAsync(id, dto);
            return Ok(transfer);
        }

        [HttpPost("{id}/dispatch")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> DispatchTransfer(Guid id)
        {
            var transfer = await _stockTransferService.DispatchAsync(id);
            return Ok(transfer);
        }

        [HttpPost("{id}/receive")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> ReceiveTransfer(Guid id, [FromForm] ReceiveStockTransferDto dto)
        {
            var transfer = await _stockTransferService.ReceiveAsync(id, dto);
            return Ok(transfer);
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "WarehouseStaff,WarehouseManager,CEO,Admin")]
        public async Task<IActionResult> CancelTransfer(Guid id)
        {
            var transfer = await _stockTransferService.CancelAsync(id);
            return Ok(transfer);
        }
    }
}
