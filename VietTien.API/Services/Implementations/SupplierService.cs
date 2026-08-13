using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Supplier;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SupplierService : ISupplierService
    {
        private readonly ApplicationDbContext _context;

        public SupplierService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SupplierDto>> GetAllAsync()
        {
            var suppliers = await _context.Suppliers.ToListAsync();
            return suppliers.Select(MapToDto);
        }

        public async Task<SupplierDto> GetByIdAsync(Guid id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) throw new KeyNotFoundException("Supplier not found");
            return MapToDto(supplier);
        }

        public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request)
        {
            if (await _context.Suppliers.AnyAsync(s => s.Code == request.Code))
                throw new InvalidOperationException($"Supplier with Code '{request.Code}' already exists");

            var supplier = new Supplier
            {
                Name = request.Name,
                Code = request.Code,
                ContactPerson = request.ContactPerson,
                Phone = request.Phone,
                Email = request.Email,
                Address = request.Address,
                TaxCode = request.TaxCode,
                IsActive = true
            };

            _context.Suppliers.Add(supplier);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Lưới an toàn cuối cho race check-then-insert ở AnyAsync(Code) phía trên: 2 request đồng
                // thời cùng tạo nhà cung cấp trùng mã đều pass check rồi mới đụng unique index lúc
                // SaveChanges -> request thua cuộc nhận lỗi rõ ràng thay vì 500 chung chung.
                throw new InvalidOperationException($"Supplier with Code '{request.Code}' already exists");
            }

            return MapToDto(supplier);
        }

        public async Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest request)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) throw new KeyNotFoundException("Supplier not found");

            if (supplier.Code != request.Code && await _context.Suppliers.AnyAsync(s => s.Code == request.Code))
                throw new InvalidOperationException($"Supplier with Code '{request.Code}' already exists");

            supplier.Name = request.Name;
            supplier.Code = request.Code;
            supplier.ContactPerson = request.ContactPerson;
            supplier.Phone = request.Phone;
            supplier.Email = request.Email;
            supplier.Address = request.Address;
            supplier.TaxCode = request.TaxCode;
            supplier.IsActive = request.IsActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new InvalidOperationException($"Supplier with Code '{request.Code}' already exists");
            }

            return MapToDto(supplier);
        }

        // SQL Server error 2601 (unique index) / 2627 (unique constraint) — dùng để phân biệt vi phạm
        // unique index (Supplier.Code) khỏi các lỗi DbUpdateException khác không nên bị nuốt thành 409.
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

        private static SupplierDto MapToDto(Supplier s)
        {
            return new SupplierDto
            {
                Id = s.Id,
                Name = s.Name,
                Code = s.Code,
                ContactPerson = s.ContactPerson,
                Phone = s.Phone,
                Email = s.Email,
                Address = s.Address,
                TaxCode = s.TaxCode,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            };
        }
    }
}
