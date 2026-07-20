using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VietTien.API.Controllers
{
    [Route("api/handover-records")]
    [ApiController]
    [Authorize]
    public class HandoverController : ControllerBase
    {
        [HttpGet("{id}")]
        [Authorize(Roles = "WarehouseStaff,Sales,WarehouseManager")]
        public IActionResult GetHandoverById(Guid id)
        {
            // TODO: Implement logic to get handover details
            return Ok(new { id, status = "PENDING" });
        }

        [HttpPost]
        [Authorize(Roles = "Sales,WarehouseStaff")]
        public IActionResult CreateHandover()
        {
            // TODO: Implement logic to create a new handover record
            return Ok(new { message = "Handover record created successfully", id = Guid.NewGuid() });
        }

        [HttpPost("{id}/warehouse-confirm")]
        [Authorize(Roles = "WarehouseStaff")]
        public IActionResult WarehouseConfirm(Guid id)
        {
            // TODO: Implement dual confirmation for warehouse side
            return Ok(new { message = "Warehouse handover confirmed" });
        }

        [HttpPost("{id}/sales-confirm")]
        [Authorize(Roles = "Sales")]
        public IActionResult SalesConfirm(Guid id)
        {
            // TODO: Implement dual confirmation for sales side
            return Ok(new { message = "Sales handover confirmed" });
        }
    }
}
