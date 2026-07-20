using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/purchase-orders")]
    [ApiController]
    [Authorize]
    public class PurchaseOrderController : ControllerBase
    {
        private readonly IPurchaseOrderService _poService;
        private readonly IGoodsReceiptService _receiptService;
        private readonly VietTien.API.Data.ApplicationDbContext _context;

        public PurchaseOrderController(IPurchaseOrderService poService, IGoodsReceiptService receiptService, VietTien.API.Data.ApplicationDbContext context)
        {
            _poService = poService;
            _receiptService = receiptService;
            _context = context;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("warehouses")]
        [Authorize(Roles = "CEO,WarehouseStaff,Admin")]
        public async Task<IActionResult> GetWarehouses()
        {
            var warehouses = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(_context.Warehouses);
            if (!warehouses.Any())
            {
                var defaultWh = new VietTien.API.Models.Warehouse
                {
                    Code = "WH-PROD",
                    Name = "Kho Thành Phẩm (WH-PROD)"
                };
                _context.Warehouses.Add(defaultWh);
                await _context.SaveChangesAsync();
                warehouses.Add(defaultWh);
            }
            return Ok(warehouses.Select(w => new { w.Id, w.Name, w.Code }));
        }

        [HttpGet]
        [Authorize(Roles = "CEO,WarehouseStaff")]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var result = await _poService.GetAllAsync(status);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "CEO,WarehouseStaff")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _poService.GetByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseOrderRequest request)
        {
            try
            {
                var result = await _poService.CreateAsync(GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("import/excel")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> ImportFromExcel(Microsoft.AspNetCore.Http.IFormFile file)
        {
            try
            {
                var result = await _poService.ImportFromExcelAsync(file, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("import/image")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> ImportFromImage(Microsoft.AspNetCore.Http.IFormFile file)
        {
            try
            {
                var result = await _poService.ImportFromImageAsync(file, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] CreatePurchaseOrderRequest request)
        {
            try
            {
                var result = await _poService.UpdateDraftAsync(id, GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/issue")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> Issue(Guid id)
        {
            try
            {
                var result = await _poService.IssueAsync(id, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/send-to-warehouse")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> SendToWarehouse(Guid id)
        {
            try
            {
                var result = await _poService.SendToWarehouseAsync(id, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            try
            {
                var result = await _poService.CancelAsync(id, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/resolve-discrepancy")]
        [HttpPost("/api/receiving-discrepancies/{id}/decision")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> ResolveDiscrepancy(Guid id, [FromBody] DiscrepancyResolutionRequest request)
        {
            try
            {
                var result = await _poService.ResolveDiscrepancyAsync(id, GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/close")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> Close(Guid id)
        {
            try
            {
                var result = await _poService.ClosePurchaseOrderAsync(id, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // --- Goods Receipt Endpoints ---
        
        [HttpGet("{id}/receipts")]
        [Authorize(Roles = "CEO,WarehouseStaff")]
        public async Task<IActionResult> GetReceipts(Guid id)
        {
            try
            {
                var result = await _receiptService.GetByPurchaseOrderIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/receipts")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> CreateReceipt(Guid id, [FromBody] CreateGoodsReceiptRequest request)
        {
            try
            {
                var result = await _receiptService.CreateFromPOAsync(id, GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/receipts/{rId}/post")]
        [Authorize(Roles = "WarehouseStaff")]
        public async Task<IActionResult> PostReceipt(Guid id, Guid rId)
        {
            try
            {
                var result = await _receiptService.PostReceiptAsync(rId, GetUserId());
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
