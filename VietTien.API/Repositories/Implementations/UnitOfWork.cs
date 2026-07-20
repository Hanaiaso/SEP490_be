using VietTien.API.Data;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;
        private IUserRepository? _users;
        private ICartRepository? _carts;
        private IOrderRepository? _orders;
        private IProductRepository? _products;
        private IAddressRepository? _addresses;

        public UnitOfWork(ApplicationDbContext context)
        {
            _context = context;
        }

        public IUserRepository Users
            => _users ??= new UserRepository(_context);

        public ICartRepository Carts
            => _carts ??= new CartRepository(_context);
        public IOrderRepository Orders
            => _orders ??= new OrderRepository(_context);
        public IProductRepository Products
            => _products ??= new ProductRepository(_context);

        public IAddressRepository Addresses
            => _addresses ??= new AddressRepository(_context);

        public async Task<int> SaveChangesAsync()
            => await _context.SaveChangesAsync();

        public void Dispose()
            => _context.Dispose();
    }
}
