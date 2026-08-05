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
            await _context.SaveChangesAsync();

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

            await _context.SaveChangesAsync();

            return MapToDto(supplier);
        }

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
