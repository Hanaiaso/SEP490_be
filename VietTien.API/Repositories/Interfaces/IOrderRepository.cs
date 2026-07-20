using VietTien.API.DTOs.Order;
using VietTien.API.Models;

namespace VietTien.API.Repositories.Interfaces
{
    public interface IOrderRepository
    {
        Task<Order> CreateOrderAsync(Order order);
        Task<Order?> GetOrderByIdAsync(Guid orderId);
        Task<Order?> GetOrderByCodeAsync(string orderCode);
        Task UpdateOrderAsync(Order order);
        Task<List<Order>> GetAllOrdersWithCustomersAsync();

        /// <summary>
        /// Lấy danh sách lịch sử đơn hàng của Customer có lọc, tìm kiếm và phân trang.
        /// Chỉ trả đơn thuộc CustomerProfileId được chỉ định (BR-OH-02, BR-OH-07).
        /// </summary>
        Task<(List<Order> Items, int TotalCount)> GetOrderHistoryAsync(
            Guid customerProfileId, OrderHistoryQueryDto query);

        /// <summary>
        /// Lấy chi tiết đơn hàng, đồng thời kiểm tra quyền sở hữu (BR-OH-02).
        /// Trả về null nếu không tìm thấy hoặc đơn không thuộc customerProfileId.
        /// </summary>
        Task<Order?> GetOrderDetailForCustomerAsync(Guid orderId, Guid customerProfileId);

        /// <summary>
        /// Lấy các đơn đã thanh toán (Paid) của Customer từ mốc thời gian fromUtc trở đi,
        /// kèm OrderItems và Product – phục vụ thống kê chi tiêu cá nhân.
        /// </summary>
        Task<List<Order>> GetPaidOrdersForStatsAsync(Guid customerProfileId, DateTime fromUtc);
    }
}
