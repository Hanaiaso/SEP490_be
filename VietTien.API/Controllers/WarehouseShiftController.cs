using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;

namespace VietTien.API.Controllers
{
    [Route("api/warehouse-shifts")]
    [ApiController]
    [Authorize]
    public class WarehouseShiftController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WarehouseShiftController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<List<WarehouseShiftDto>>> GetShifts()
        {
            var shifts = await _context.WarehouseShifts.ToListAsync();
            var dtoList = shifts.Select(s => new WarehouseShiftDto
            {
                Id = s.Id,
                Name = s.Name,
                StartTime = s.StartTime.ToString(@"hh\:mm"),
                EndTime = s.EndTime.ToString(@"hh\:mm"),
                Description = s.Description
            }).ToList();

            return Ok(dtoList);
        }
    }
}
