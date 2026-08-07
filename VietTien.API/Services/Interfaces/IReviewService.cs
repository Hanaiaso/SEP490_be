using VietTien.API.DTOs.Review;

namespace VietTien.API.Services.Interfaces
{
    public interface IReviewService
    {
        Task<IEnumerable<ProductReviewDto>> GetProductReviewsAsync(Guid productId, int page = 1, int pageSize = 20);
        Task<ProductReviewSummaryDto> GetSummaryAsync(Guid productId);
        Task<ReviewEligibilityDto> GetEligibilityAsync(Guid productId, Guid userId);
        Task<ProductReviewDto> CreateReviewAsync(Guid productId, Guid userId, CreateReviewRequest request);
        Task<ProductReviewDto> UpdateReviewAsync(Guid reviewId, Guid userId, UpdateReviewRequest request);
        Task DeleteReviewAsync(Guid reviewId, Guid userId);

        /// <summary>Danh sách đánh giá cho Sales/Admin xử lý phản hồi. SalesStaff chỉ thấy đánh giá của khách mình phụ trách (theo Order.SalesStaffId); Admin thấy tất cả.</summary>
        Task<IEnumerable<ProductReviewDto>> GetReviewsForSalesAsync(Guid staffUserId, string staffRole);

        /// <summary>Sales phụ trách/Admin phản hồi công khai 1 đánh giá.</summary>
        Task<ProductReviewDto> ReplyToReviewAsync(Guid reviewId, Guid staffUserId, string staffRole, ReplyReviewRequest request);
    }
}
