using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class WarehouseManagementService : IWarehouseManagementService
    {
        private readonly ApplicationDbContext _context;

        public WarehouseManagementService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<WarehouseDto>> GetAllWarehousesAsync()
        {
            return await _context.Warehouses
                .Include(w => w.Locations)
                .Select(w => new WarehouseDto
                {
                    Id = w.Id,
                    Name = w.Name,
                    Code = w.Code,
                    Locations = w.Locations.Select(l => new LocationDto
                    {
                        Id = l.Id,
                        Name = l.Name,
                        Type = l.Type
                    }).ToList()
                })
                .ToListAsync();
        }

        public async Task<WarehouseDto> GetWarehouseByIdAsync(Guid id)
        {
            var warehouse = await _context.Warehouses
                .Include(w => w.Locations)
                .FirstOrDefaultAsync(w => w.Id == id);
            
            if (warehouse == null)
            {
                throw new Exception("Không tìm thấy kho hàng.");
            }

            return new WarehouseDto
            {
                Id = warehouse.Id,
                Name = warehouse.Name,
                Code = warehouse.Code,
                Locations = warehouse.Locations.Select(l => new LocationDto
                {
                    Id = l.Id,
                    Name = l.Name,
                    Type = l.Type
                }).ToList()
            };
        }

        public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto)
        {
            if (await _context.Warehouses.AnyAsync(w => w.Code == dto.Code))
            {
                throw new Exception("Mã kho đã tồn tại trong hệ thống.");
            }

            var warehouse = new Warehouse
            {
                Name = dto.Name,
                Code = dto.Code
            };

            if (dto.LocationNames != null && dto.LocationNames.Any())
            {
                foreach (var locName in dto.LocationNames)
                {
                    if (!string.IsNullOrWhiteSpace(locName))
                    {
                        warehouse.Locations.Add(new WarehouseLocation
                        {
                            Id = Guid.Empty, // Force EF to treat as new entity
                            Name = locName.Trim(),
                            Type = "Normal"
                        });
                    }
                }
            }

            // Người dùng không còn nhập vị trí lưu trữ lúc tạo kho -> đảm bảo kho luôn có ít nhất 1
            // vị trí "Normal" để các luồng nhập/thêm hàng sau này có chỗ gán tồn kho vào.
            if (!warehouse.Locations.Any(l => l.Type == "Normal"))
            {
                warehouse.Locations.Add(new WarehouseLocation
                {
                    Id = Guid.Empty,
                    Name = "Vị trí mặc định",
                    Type = "Normal"
                });
            }

            _context.Warehouses.Add(warehouse);
            await _context.SaveChangesAsync();

            return await GetWarehouseByIdAsync(warehouse.Id);
        }

        public async Task<WarehouseDto> UpdateWarehouseAsync(Guid id, UpdateWarehouseDto dto)
        {
            var warehouse = await _context.Warehouses
                .Include(w => w.Locations)
                .FirstOrDefaultAsync(w => w.Id == id);
                
            if (warehouse == null)
            {
                throw new Exception("Không tìm thấy kho hàng.");
            }

            if (await _context.Warehouses.AnyAsync(w => w.Code == dto.Code && w.Id != id))
            {
                throw new Exception("Mã kho đã tồn tại trong hệ thống.");
            }

            warehouse.Name = dto.Name;
            warehouse.Code = dto.Code;

            if (dto.LocationNames != null)
            {
                var inputNames = dto.LocationNames.Select(n => n.Trim()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var existingLocs = warehouse.Locations.ToList();

                // 1. Find exact matches
                var exactMatches = existingLocs.Where(l => inputNames.Contains(l.Name)).ToList();
                
                var unmatchedExisting = existingLocs.Except(exactMatches).ToList();
                var unmatchedInput = inputNames.Except(exactMatches.Select(l => l.Name)).ToList();

                // 2. Try to update unmatched existing with unmatched input (rename)
                int renameCount = Math.Min(unmatchedExisting.Count, unmatchedInput.Count);
                for (int i = 0; i < renameCount; i++)
                {
                    var locToUpdate = unmatchedExisting[i];
                    locToUpdate.Name = unmatchedInput[i];
                    _context.Entry(locToUpdate).State = EntityState.Modified;
                }

                // 3. Any remaining unmatched existing should be deleted
                for (int i = renameCount; i < unmatchedExisting.Count; i++)
                {
                    var locToDelete = unmatchedExisting[i];
                    var hasInventory = await _context.Inventories.AnyAsync(inv => inv.WarehouseLocationId == locToDelete.Id);
                    if (hasInventory)
                    {
                        throw new Exception($"Không thể xóa vị trí '{locToDelete.Name}' vì đang chứa hàng tồn kho.");
                    }
                    warehouse.Locations.Remove(locToDelete);
                    _context.Entry(locToDelete).State = EntityState.Deleted;
                }

                // 4. Any remaining unmatched input should be added
                for (int i = renameCount; i < unmatchedInput.Count; i++)
                {
                    var newLoc = new WarehouseLocation
                    {
                        Id = Guid.Empty,
                        Name = unmatchedInput[i],
                        Type = "Normal"
                    };
                    warehouse.Locations.Add(newLoc);
                    _context.Entry(newLoc).State = EntityState.Added;
                }
            }

            await _context.SaveChangesAsync();

            return await GetWarehouseByIdAsync(warehouse.Id);
        }

        public async Task DeleteWarehouseAsync(Guid id)
        {
            var warehouse = await _context.Warehouses
                .Include(w => w.Locations)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (warehouse == null)
            {
                throw new Exception("Không tìm thấy kho hàng.");
            }

            // Check if there is any inventory in this warehouse
            var hasInventory = await _context.Inventories.AnyAsync(i => i.WarehouseLocation.WarehouseId == id);
            if (hasInventory)
            {
                throw new Exception("Không thể xóa kho đang chứa hàng tồn kho. Vui lòng chuyển hoặc xuất hết hàng trước khi xóa.");
            }

            _context.Warehouses.Remove(warehouse);
            await _context.SaveChangesAsync();
        }
    }
}
