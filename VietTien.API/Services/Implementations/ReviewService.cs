using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Review;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class ReviewService : IReviewService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public ReviewService(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<IEnumerable<ProductReviewDto>> GetProductReviewsAsync(Guid productId, int page = 1, int pageSize = 20)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

            var reviews = await _context.ProductReviews
                .AsNoTracking()
                .Where(r => r.ProductId == productId)
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(r => r.RepliedByUser)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return reviews.Select(MapToDto);
        }

        public async Task<IEnumerable<ProductReviewDto>> GetReviewsForSalesAsync(Guid staffUserId, string staffRole)
        {
            var query = _context.ProductReviews
                .AsNoTracking()
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(r => r.Product)
                .Include(r => r.RepliedByUser)
                .Include(r => r.Order)
                .AsQueryable();

            if (staffRole == "SalesStaff")
                query = query.Where(r => r.Order.SalesStaffId == staffUserId);

            var reviews = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            return reviews.Select(MapToDto);
        }

        public async Task<ProductReviewDto> ReplyToReviewAsync(Guid reviewId, Guid staffUserId, string staffRole, ReplyReviewRequest request)
        {
            var review = await _context.ProductReviews
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(r => r.Product)
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == reviewId);

            if (review == null) throw new KeyNotFoundException("Không tìm thấy đánh giá.");

            if (staffRole == "SalesStaff" && review.Order.SalesStaffId != staffUserId)
                throw new UnauthorizedAccessException("Bạn chỉ được phản hồi đánh giá của khách hàng do mình phụ trách.");

            review.ReplyText = request.ReplyText;
            review.RepliedByUserId = staffUserId;
            review.RepliedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var staffUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffUserId);

            try
            {
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_33_ReviewReplyPosted,
                    review.CustomerProfile.UserId,
                    "Đánh giá của bạn đã được phản hồi",
                    $"Việt Tiến đã phản hồi đánh giá của bạn cho sản phẩm {review.Product.Name}.",
                    review.Id, "ProductReview");
            }
            catch (Exception)
            {
                // Best-effort: gửi thông báo thất bại không được làm rollback phản hồi đã lưu.
            }

            return new ProductReviewDto
            {
                Id = review.Id,
                ProductId = review.ProductId,
                ProductName = review.Product.Name,
                CustomerName = review.CustomerProfile.User?.FullName ?? "Khách hàng",
                Rating = review.Rating,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
                ReplyText = review.ReplyText,
                RepliedByName = staffUser?.FullName,
                RepliedAt = review.RepliedAt
            };
        }

        public async Task<ProductReviewSummaryDto> GetSummaryAsync(Guid productId)
        {
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null) throw new KeyNotFoundException("Không tìm thấy sản phẩm.");

            return new ProductReviewSummaryDto
            {
                AverageRating = product.AverageRating,
                ReviewCount = product.ReviewCount
            };
        }

        public async Task<ReviewEligibilityDto> GetEligibilityAsync(Guid productId, Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);

            var existing = await _context.ProductReviews
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CustomerProfileId == profile.Id && r.ProductId == productId);

            if (existing != null)
                return new ReviewEligibilityDto { CanReview = false, AlreadyReviewed = true, ExistingReviewId = existing.Id };

            var hasCompletedOrder = await _context.Orders
                .AsNoTracking()
                .AnyAsync(o => o.CustomerProfileId == profile.Id
                    && o.OrderStatus == OrderStatus.Completed
                    && o.OrderItems.Any(oi => oi.ProductId == productId));

            return new ReviewEligibilityDto { CanReview = hasCompletedOrder, AlreadyReviewed = false };
        }

        public async Task<ProductReviewDto> CreateReviewAsync(Guid productId, Guid userId, CreateReviewRequest request)
        {
            var profile = await GetCustomerProfileAsync(userId);

            if (await _context.ProductReviews.AnyAsync(r => r.CustomerProfileId == profile.Id && r.ProductId == productId))
                throw new InvalidOperationException("Bạn đã đánh giá sản phẩm này rồi. Vui lòng sửa đánh giá cũ.");

            var completedOrder = await _context.Orders
                .Where(o => o.CustomerProfileId == profile.Id
                    && o.OrderStatus == OrderStatus.Completed
                    && o.OrderItems.Any(oi => oi.ProductId == productId))
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (completedOrder == null)
                throw new InvalidOperationException("Bạn chỉ có thể đánh giá sản phẩm đã mua và nhận hàng thành công.");

            var review = new ProductReview
            {
                ProductId = productId,
                CustomerProfileId = profile.Id,
                OrderId = completedOrder.Id,
                Rating = request.Rating,
                Comment = request.Comment
            };

            _context.ProductReviews.Add(review);
            await _context.SaveChangesAsync();
            await RecalculateProductRatingAsync(productId);

            return await MapToDtoWithUserAsync(review, profile);
        }

        public async Task<ProductReviewDto> UpdateReviewAsync(Guid reviewId, Guid userId, UpdateReviewRequest request)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var review = await _context.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
            if (review == null) throw new KeyNotFoundException("Không tìm thấy đánh giá.");
            if (review.CustomerProfileId != profile.Id) throw new UnauthorizedAccessException("Bạn không có quyền sửa đánh giá này.");

            review.Rating = request.Rating;
            review.Comment = request.Comment;
            review.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await RecalculateProductRatingAsync(review.ProductId);

            return await MapToDtoWithUserAsync(review, profile);
        }

        public async Task DeleteReviewAsync(Guid reviewId, Guid userId)
        {
            var profile = await GetCustomerProfileAsync(userId);
            var review = await _context.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
            if (review == null) throw new KeyNotFoundException("Không tìm thấy đánh giá.");
            if (review.CustomerProfileId != profile.Id) throw new UnauthorizedAccessException("Bạn không có quyền xoá đánh giá này.");

            var productId = review.ProductId;
            _context.ProductReviews.Remove(review);
            await _context.SaveChangesAsync();
            await RecalculateProductRatingAsync(productId);
        }

        private async Task<CustomerProfile> GetCustomerProfileAsync(Guid userId)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(cp => cp.UserId == userId);
            if (profile == null) throw new KeyNotFoundException("Không tìm thấy hồ sơ khách hàng.");
            return profile;
        }

        private async Task RecalculateProductRatingAsync(Guid productId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null) return;

            var ratings = await _context.ProductReviews
                .Where(r => r.ProductId == productId)
                .Select(r => r.Rating)
                .ToListAsync();

            product.ReviewCount = ratings.Count;
            product.AverageRating = ratings.Count > 0 ? Math.Round(ratings.Average(), 1) : 0;

            await _context.SaveChangesAsync();
        }

        private static ProductReviewDto MapToDto(ProductReview r) => new ProductReviewDto
        {
            Id = r.Id,
            ProductId = r.ProductId,
            ProductName = r.Product?.Name,
            CustomerName = r.CustomerProfile?.User?.FullName ?? "Khách hàng",
            Rating = r.Rating,
            Comment = r.Comment,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            ReplyText = r.ReplyText,
            RepliedByName = r.RepliedByUser?.FullName,
            RepliedAt = r.RepliedAt
        };

        private async Task<ProductReviewDto> MapToDtoWithUserAsync(ProductReview r, CustomerProfile profile)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profile.UserId);
            return new ProductReviewDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                CustomerName = user?.FullName ?? "Khách hàng",
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                ReplyText = r.ReplyText,
                RepliedAt = r.RepliedAt
            };
        }
    }
}
