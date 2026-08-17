using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Material;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class MaterialService : IMaterialService
    {
        private readonly ApplicationDbContext _context;

        public MaterialService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MaterialDto>> GetAllAsync(string? search = null)
        {
            var query = _context.Materials.Include(m => m.Inventories).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(m => m.Name.ToLower().Contains(lowerSearch));
            }

            var materials = await query.OrderBy(m => m.Name).ToListAsync();

            return materials.Select(MapToDto);
        }

        public async Task<MaterialDto> GetByIdAsync(Guid id)
        {
            var material = await _context.Materials.Include(m => m.Inventories).FirstOrDefaultAsync(m => m.Id == id);
            if (material == null)
            {
                throw new KeyNotFoundException("Không tìm thấy nguyên liệu.");
            }

            return MapToDto(material);
        }

        public async Task<MaterialDto> CreateAsync(CreateMaterialDto dto)
        {
            var exists = await _context.Materials.AnyAsync(m => m.Name.ToLower() == dto.Name.ToLower());
            if (exists)
            {
                throw new InvalidOperationException("Tên nguyên liệu đã tồn tại.");
            }

            var material = new Material
            {
                Id = Guid.NewGuid(),
                Name = dto.Name,
                Unit = dto.Unit,
                SafetyThreshold = dto.SafetyThreshold,
                MaxStockThreshold = dto.MaxStockThreshold,
                CurrentStock = 0 // Tồn kho ban đầu là 0
            };

            _context.Materials.Add(material);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Lưới an toàn cuối cho race check-then-insert ở AnyAsync(Name) phía trên: 2 request đồng
                // thời cùng tạo nguyên liệu trùng tên đều pass check rồi mới đụng unique index lúc
                // SaveChanges -> request thua cuộc nhận lỗi rõ ràng thay vì 500 chung chung.
                throw new InvalidOperationException("Tên nguyên liệu đã tồn tại.");
            }

            return MapToDto(material);
        }

        public async Task<MaterialDto> UpdateAsync(Guid id, UpdateMaterialDto dto)
        {
            // Include Inventories để MapToDto tính đúng CurrentStock hiện tại thay vì rơi về
            // Material.CurrentStock (luôn = 0, không được cập nhật ở đâu khác trong hệ thống).
            var material = await _context.Materials
                .Include(m => m.Inventories)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (material == null)
            {
                throw new KeyNotFoundException("Không tìm thấy nguyên liệu.");
            }

            var exists = await _context.Materials.AnyAsync(m => m.Id != id && m.Name.ToLower() == dto.Name.ToLower());
            if (exists)
            {
                throw new InvalidOperationException("Tên nguyên liệu đã tồn tại.");
            }

            material.Name = dto.Name;
            material.Unit = dto.Unit;
            material.SafetyThreshold = dto.SafetyThreshold;
            material.MaxStockThreshold = dto.MaxStockThreshold;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new InvalidOperationException("Tên nguyên liệu đã tồn tại.");
            }

            return MapToDto(material);
        }

        // SQL Server error 2601 (unique index) / 2627 (unique constraint) — dùng để phân biệt vi phạm
        // unique index (Material.Name) khỏi các lỗi DbUpdateException khác không nên bị nuốt thành 409.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

        public async Task DeleteAsync(Guid id)
        {
            var material = await _context.Materials
                .Include(m => m.Inventories)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (material == null)
            {
                throw new KeyNotFoundException("Không tìm thấy nguyên liệu.");
            }

            if (material.Inventories.Any())
            {
                throw new InvalidOperationException("Không thể xóa nguyên liệu đã có dữ liệu tồn kho. Vui lòng kiểm tra lại.");
            }

            // Kiểm tra các bản ghi lịch sử liên quan (PO, Receipt, Issue, Transfer, Quarantine) nếu cần
            // Ở đây đơn giản chỉ check Inventory. Trong thực tế có thể dùng Soft Delete cho Material giống Product.

            _context.Materials.Remove(material);
            await _context.SaveChangesAsync();
        }

        private static MaterialDto MapToDto(Material m)
        {
            var calculatedStock = m.Inventories != null && m.Inventories.Any() 
                ? m.Inventories.Sum(i => i.AvailableQuantity) 
                : m.CurrentStock;

            return new MaterialDto
            {
                Id = m.Id,
                Name = m.Name,
                Unit = m.Unit,
                CurrentStock = calculatedStock,
                SafetyThreshold = m.SafetyThreshold,
                MaxStockThreshold = m.MaxStockThreshold,
                LastAlertSentDate = m.LastAlertSentDate,
                IsBelowSafetyThreshold = calculatedStock <= m.SafetyThreshold
            };
        }
    }
}
