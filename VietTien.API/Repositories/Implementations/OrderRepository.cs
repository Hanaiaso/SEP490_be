using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class OrderRepository : IOrderRepository
    {
        private readonly ApplicationDbContext _context;

        public OrderRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Order> CreateOrderAsync(Order order)
        {
            await _context.Orders.AddAsync(order);
            return order;
        }

        public async Task<Order?> GetOrderByIdAsync(Guid orderId)
        {
            return await _context.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Transactions)
                .FirstOrDefaultAsync(o => o.Id == orderId);
        }

        public async Task<Order?> GetOrderByCodeAsync(string orderCode)
        {
            var normalizedSearchCode = orderCode.Replace("-", "").Replace("_", "").ToUpper();
            return await _context.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Transactions)
                .FirstOrDefaultAsync(o => o.OrderCode.Replace("-", "").Replace("_", "") == normalizedSearchCode);
        }

        public Task UpdateOrderAsync(Order order)
        {
            _context.Orders.Update(order);
            return Task.CompletedTask;
        }

        public async Task<List<Order>> GetAllOrdersWithCustomersAsync()
        {
            return await _context.Orders
                .Include(o => o.CustomerProfile)
                .Include(o => o.OrderItems)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<(List<Order> Items, int TotalCount)> GetOrderHistoryAsync(
            Guid customerProfileId, OrderHistoryQueryDto query)
        {
            // Chỉ lấy đơn thuộc đúng CustomerProfileId (BR-OH-02)
            var q = _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerProfileId == customerProfileId);

            // Tìm kiếm theo OrderCode (BR-OH-07)
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var normalized = query.Search.Trim().ToUpper().Replace("-", "").Replace("_", "");
                q = q.Where(o => o.OrderCode.Replace("-", "").Replace("_", "").Contains(normalized));
            }

            // Lọc theo OrderStatus (BR-OH-07)
            if (!string.IsNullOrWhiteSpace(query.Status) &&
                query.Status.ToLower() != "all" &&
                Enum.TryParse<OrderStatus>(query.Status, true, out var parsedStatus))
            {
                q = q.Where(o => o.OrderStatus == parsedStatus);
            }

            // Lọc theo PaymentStatus (BR-OH-07)
            if (!string.IsNullOrWhiteSpace(query.PaymentStatus) &&
                query.PaymentStatus.ToLower() != "all" &&
                Enum.TryParse<PaymentStatus>(query.PaymentStatus, true, out var parsedPayStatus))
            {
                q = q.Where(o => o.PaymentStatus == parsedPayStatus);
            }

            // Lọc theo khoảng ngày (BR-OH-07)
            if (query.FromDate.HasValue)
                q = q.Where(o => o.CreatedAt >= query.FromDate.Value.ToUniversalTime());

            if (query.ToDate.HasValue)
                q = q.Where(o => o.CreatedAt <= query.ToDate.Value.ToUniversalTime().AddDays(1).AddTicks(-1));

            // Tổng số bản ghi (phân trang – NFR-OH-01)
            var totalCount = await q.CountAsync();

            // Sắp xếp mới nhất trước (BR-OH-17), phân trang tại DB (NFR-OH-01)
            var pageSize = Math.Max(1, Math.Min(query.PageSize, 50));
            var page     = Math.Max(1, query.Page);

            var items = await q
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(o => o.OrderItems)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<List<Order>> GetPaidOrdersForStatsAsync(Guid customerProfileId, DateTime fromUtc)
        {
            return await _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerProfileId == customerProfileId
                            && o.PaymentStatus == PaymentStatus.Paid
                            && o.CreatedAt >= fromUtc)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<Order?> GetOrderDetailForCustomerAsync(Guid orderId, Guid customerProfileId)
        {
            // Kiểm tra quyền sở hữu tại DB query (BR-OH-02, NFR-OH-03)
            return await _context.Orders
                .AsNoTracking()
                .AsSplitQuery() // Tách query cho từng Include collection (Addresses, OrderItems, ReturnedGoodsLogs, ReturnExchangeRequests...) để tránh nhân bản dòng kiểu tích Descartes khi JOIN nhiều bảng 1-nhiều cùng lúc
                .Where(o => o.Id == orderId && o.CustomerProfileId == customerProfileId)
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.User)
                .Include(o => o.CustomerProfile)
                    .ThenInclude(cp => cp.Addresses)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)  // Include Product để lấy tên, SKU (BR-OH-09)
                .Include(o => o.DeliveryTrip)  // Giờ dự kiến xuất phát/đến (Nhóm C) hiển thị cho khách hàng
                .Include(o => o.ReturnedGoodsLogs)
                .Include(o => o.ReturnExchangeRequests)
                    .ThenInclude(req => req.ReturnItems)
                        .ThenInclude(ri => ri.Product)
                .Include(o => o.ReturnExchangeRequests)
                    .ThenInclude(req => req.ExchangeItems)
                        .ThenInclude(ei => ei.Product)
                .FirstOrDefaultAsync();
        }
    }
}
