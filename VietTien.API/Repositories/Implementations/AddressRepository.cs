using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class AddressRepository : IAddressRepository
    {
        private readonly ApplicationDbContext _context;

        public AddressRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Address>> GetByCustomerProfileIdAsync(Guid customerProfileId)
        {
            return await _context.Addresses
                .Where(a => a.CustomerProfileId == customerProfileId)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<Address?> GetByIdAsync(Guid id)
        {
            return await _context.Addresses.FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task AddAsync(Address address)
        {
            await _context.Addresses.AddAsync(address);
        }

        public void Update(Address address)
        {
            _context.Addresses.Update(address);
        }

        public void Delete(Address address)
        {
            _context.Addresses.Remove(address);
        }

        public async Task<int> CountByCustomerProfileIdAsync(Guid customerProfileId)
        {
            return await _context.Addresses.CountAsync(a => a.CustomerProfileId == customerProfileId);
        }

        public async Task ResetDefaultAsync(Guid customerProfileId)
        {
            var defaultAddresses = await _context.Addresses
                .Where(a => a.CustomerProfileId == customerProfileId && a.IsDefault)
                .ToListAsync();

            foreach (var addr in defaultAddresses)
            {
                addr.IsDefault = false;
            }
        }
    }
}
