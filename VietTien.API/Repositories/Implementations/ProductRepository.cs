using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class ProductRepository : IProductRepository
    {
        private readonly ApplicationDbContext _context;

        public ProductRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<(IEnumerable<Product> Items, int TotalCount)> GetAllAsync(
            int page,
            int pageSize,
            Guid? categoryId = null,
            string? searchKeyword = null,
            string? sortBy = null)
        {
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .Where(p => !p.IsDiscontinued)
                .AsQueryable();

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var keyword = searchKeyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(keyword) ||
                    p.Sku.ToLower().Contains(keyword));
            }

            var totalCount = await query.CountAsync();

            if (sortBy == "sales")
            {
                query = query.OrderByDescending(p => p.OrderItems.Sum(oi => oi.Quantity));
            }
            else if (sortBy == "newest")
            {
                // Fallback to sorting by Id descending if CreatedAt is not available
                query = query.OrderByDescending(p => p.Id);
            }
            else
            {
                query = query.OrderBy(p => p.Name);
            }

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<Product?> GetByIdAsync(Guid id)
        {
            return await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDiscontinued);
        }

        public async Task<Product?> GetByIdIncludingDiscontinuedAsync(Guid id)
        {
            return await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<Category>> GetActiveCategoriesAsync()
        {
            return await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task AddAsync(Product product)
        {
            await _context.Products.AddAsync(product);
        }

        public async Task<(IEnumerable<Product> Items, int TotalCount)> GetAllForManagementAsync(
            int page,
            int pageSize,
            Guid? categoryId = null,
            string? searchKeyword = null,
            bool? isDiscontinued = null)
        {
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .AsQueryable();

            if (isDiscontinued.HasValue)
                query = query.Where(p => p.IsDiscontinued == isDiscontinued.Value);

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var keyword = searchKeyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(keyword) ||
                    p.Sku.ToLower().Contains(keyword));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
    }
}
