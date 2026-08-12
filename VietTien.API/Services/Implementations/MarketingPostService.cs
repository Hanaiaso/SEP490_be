using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Marketing;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class MarketingPostService : IMarketingPostService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMakeWebhookService _makeWebhookService;
        private readonly INotificationService _notificationService;
        private const int MAX_SCHEDULED_POSTS = 30;

        public MarketingPostService(ApplicationDbContext context, IMakeWebhookService makeWebhookService, INotificationService notificationService)
        {
            _context = context;
            _makeWebhookService = makeWebhookService;
            _notificationService = notificationService;
        }

        public async Task<IEnumerable<MarketingPostDto>> GetPostsAsync(string? status, Guid? productId, string? role, Guid userId)
        {
            var query = _context.MarketingPosts
                .Include(p => p.Product)
                .Include(p => p.CreatedByUser)
                .Include(p => p.ApprovedByUser)
                .AsQueryable();

            if (productId.HasValue)
            {
                query = query.Where(p => p.ProductId == productId.Value);
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<MarketingPostStatus>(status, true, out var statusEnum))
            {
                query = query.Where(p => p.Status == statusEnum);
            }

            // SalesStaff chỉ thấy bài do chính mình tạo (hoặc bài đang duyệt/lịch công khai)
            if (role == "SalesStaff" || role == "SaleStaff")
            {
                query = query.Where(p => p.CreatedByUserId == userId);
            }

            var posts = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return posts.Select(MapToDto);
        }

        public async Task<MarketingPostDto> GetPostByIdAsync(Guid id)
        {
            var post = await _context.MarketingPosts
                .Include(p => p.Product)
                .Include(p => p.CreatedByUser)
                .Include(p => p.ApprovedByUser)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null) throw new Exception("Bài viết không tồn tại.");

            return MapToDto(post);
        }

        public async Task<MarketingPostDto> CreatePostAsync(CreateMarketingPostDto dto, Guid userId)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
            if (product == null) throw new Exception("Sản phẩm không tồn tại.");

            var postCode = $"MP-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";

            var post = new MarketingPost
            {
                Code = postCode,
                ProductId = dto.ProductId,
                CreatedByUserId = userId,
                PromptUsed = dto.PromptUsed,
                TemplateName = dto.TemplateName,
                Tone = dto.Tone,
                Goal = dto.Goal,
                GeneratedImageUrl = dto.GeneratedImageUrl,
                GeneratedCaption = dto.GeneratedCaption,
                SelectedImageUrl = string.IsNullOrEmpty(dto.SelectedImageUrl) ? dto.GeneratedImageUrl : dto.SelectedImageUrl,
                EditedCaption = dto.EditedCaption,
                Hashtags = dto.Hashtags,
                CtaText = dto.CtaText,
                Status = dto.SubmitImmediately ? MarketingPostStatus.Submitted : MarketingPostStatus.Draft,
                CreatedAt = DateTime.UtcNow
            };

            _context.MarketingPosts.Add(post);
            await _context.SaveChangesAsync();

            if (post.Status == MarketingPostStatus.Submitted)
                await NotifyPendingApprovalAsync(post);

            return await GetPostByIdAsync(post.Id);
        }

        private static readonly HashSet<string> MarketingManagementRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "SalesManager", "SaleManager", "Admin"
        };

        public async Task<MarketingPostDto> UpdatePostAsync(Guid id, UpdateMarketingPostDto dto, Guid userId, string userRole)
        {
            var post = await _context.MarketingPosts.FirstOrDefaultAsync(p => p.Id == id);
            if (post == null) throw new Exception("Bài viết không tồn tại.");

            if (post.CreatedByUserId != userId && !MarketingManagementRoles.Contains(userRole ?? string.Empty))
                throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa bài viết của người khác.");

            if (post.Status != MarketingPostStatus.Draft && post.Status != MarketingPostStatus.ReworkRequired)
            {
                throw new Exception("Chỉ được chỉnh sửa bài viết đang ở trạng thái Nháp hoặc Yêu cầu sửa lại.");
            }

            post.SelectedImageUrl = dto.SelectedImageUrl;
            post.EditedCaption = dto.EditedCaption;
            post.Hashtags = dto.Hashtags;
            post.CtaText = dto.CtaText;
            post.UpdatedAt = DateTime.UtcNow;

            if (dto.SubmitImmediately)
            {
                post.Status = MarketingPostStatus.Submitted;
            }

            await _context.SaveChangesAsync();

            if (dto.SubmitImmediately)
                await NotifyPendingApprovalAsync(post);

            return await GetPostByIdAsync(post.Id);
        }

        public async Task<MarketingPostDto> SubmitPostAsync(Guid id, Guid userId, string userRole)
        {
            var post = await _context.MarketingPosts.FirstOrDefaultAsync(p => p.Id == id);
            if (post == null) throw new Exception("Bài viết không tồn tại.");

            if (post.CreatedByUserId != userId && !MarketingManagementRoles.Contains(userRole ?? string.Empty))
                throw new UnauthorizedAccessException("Bạn không có quyền gửi duyệt bài viết của người khác.");

            if (post.Status != MarketingPostStatus.Draft && post.Status != MarketingPostStatus.ReworkRequired)
            {
                throw new Exception("Bài viết đã được gửi duyệt hoặc đang xử lý.");
            }

            post.Status = MarketingPostStatus.Submitted;
            post.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await NotifyPendingApprovalAsync(post);

            return await GetPostByIdAsync(post.Id);
        }

        // Bài viết đã chuyển Submitted và commit thành công ở trên -> lỗi gửi notification không
        // được làm fail request tạo/gửi bài, chỉ log để theo dõi.
        private async Task NotifyPendingApprovalAsync(MarketingPost post)
        {
            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_22_AiMarketingPendingApproval,
                    SystemRole.SalesManager,
                    "Bài marketing chờ duyệt",
                    $"Bài viết {post.Code} đang chờ duyệt trước khi đăng.",
                    post.Id,
                    "MarketingPost"
                );
            }
            catch (Exception notifyEx)
            {
                Console.WriteLine($"[MarketingPostService] Error sending marketing post pending approval notification: {notifyEx.Message}");
            }
        }

        public async Task<MarketingPostDto> MakeDecisionAsync(Guid id, MarketingPostDecisionDto dto, Guid managerId)
        {
            var post = await _context.MarketingPosts
                .Include(p => p.Product)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null) throw new Exception("Bài viết không tồn tại.");

            if (post.Status != MarketingPostStatus.Submitted)
            {
                throw new Exception("Chỉ có thể phê duyệt bài viết đang ở trạng thái Chờ duyệt (Submitted).");
            }

            post.ApprovedByUserId = managerId;
            post.UpdatedAt = DateTime.UtcNow;

            if (dto.Action.Equals("Approve", StringComparison.OrdinalIgnoreCase))
            {
                // Kiểm tra giới hạn MAX_SCHEDULED_POSTS = 30
                var activeScheduledCount = await _context.MarketingPosts
                    .CountAsync(p => p.Status == MarketingPostStatus.Scheduled || p.Status == MarketingPostStatus.Approved);

                if (activeScheduledCount >= MAX_SCHEDULED_POSTS)
                {
                    throw new Exception($"Không thể duyệt thêm bài. Đã đạt giới hạn tối đa {MAX_SCHEDULED_POSTS} bài đăng được lên lịch.");
                }

                post.ScheduledTime = dto.ScheduledTime ?? DateTime.UtcNow;
                post.Status = MarketingPostStatus.Scheduled;

                await _context.SaveChangesAsync();

                // Kích hoạt Webhook gửi thông tin sang Make.com
                await _makeWebhookService.TriggerPostToMakeAsync(post);
            }
            else if (dto.Action.Equals("ApproveNow", StringComparison.OrdinalIgnoreCase))
            {
                post.ScheduledTime = DateTime.UtcNow;
                // Chỉ chuyển Posting (chờ xác nhận thật từ Make.com qua HandleMakeWebhookCallbackAsync),
                // không set Success ngay -> tránh hiển thị "đã đăng" trong khi webhook chưa chắc đã chạy xong.
                post.Status = MarketingPostStatus.Posting;

                await _context.SaveChangesAsync();

                var triggered = await _makeWebhookService.TriggerPostToMakeAsync(post);
                if (!triggered)
                {
                    // Make.com không nhận được request -> sẽ không bao giờ gọi callback,
                    // đánh dấu thất bại ngay thay vì để bài "Posting" treo vĩnh viễn.
                    post.Status = MarketingPostStatus.PublishFailed;
                    post.PublishErrorMessage = "Không thể gửi yêu cầu đăng bài tới Make.com. Vui lòng thử lại.";
                    await _context.SaveChangesAsync();
                }
            }
            else if (dto.Action.Equals("Rework", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                {
                    throw new Exception("Vui lòng nhập lý do yêu cầu sửa lại bài viết.");
                }

                post.Status = MarketingPostStatus.ReworkRequired;
                post.RejectionReason = dto.RejectionReason;
                await _context.SaveChangesAsync();
            }
            else if (dto.Action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                {
                    throw new Exception("Vui lòng nhập lý do từ chối bài viết.");
                }

                post.Status = MarketingPostStatus.Rejected;
                post.RejectionReason = dto.RejectionReason;
                await _context.SaveChangesAsync();
            }
            else
            {
                throw new Exception("Hành động quyết định không hợp lệ.");
            }

            return await GetPostByIdAsync(post.Id);
        }

        public async Task<MarketingPostDto> HandleMakeWebhookCallbackAsync(Guid id, MakeWebhookCallbackDto dto)
        {
            var post = await _context.MarketingPosts.FirstOrDefaultAsync(p => p.Id == id);
            if (post == null) throw new Exception("Bài viết không tồn tại.");

            // Bỏ qua an toàn: callback chỉ hợp lệ khi bài đang thực sự chờ đăng (Posting).
            // Callback trùng/muộn trên bài đã Success/PublishFailed hoặc chưa từng Posting (vd Draft) không được ghi đè.
            if (post.Status != MarketingPostStatus.Posting)
                return await GetPostByIdAsync(post.Id);

            if (dto.Status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            {
                post.Status = MarketingPostStatus.Success;
                post.ExternalPostId = dto.ExternalPostId;
                post.PublishedAt = DateTime.UtcNow;
            }
            else
            {
                post.Status = MarketingPostStatus.PublishFailed;
                post.PublishErrorMessage = dto.ErrorMessage ?? "Đăng bài qua Make.com thất bại.";
            }

            post.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await GetPostByIdAsync(post.Id);
        }

        private static MarketingPostDto MapToDto(MarketingPost p)
        {
            return new MarketingPostDto
            {
                Id = p.Id,
                Code = p.Code,
                ProductId = p.ProductId,
                ProductName = p.Product?.Name ?? "",
                ProductSku = p.Product?.Sku ?? "",
                ProductImageUrl = p.Product?.ImageUrl ?? "",
                ProductPrice = p.Product?.StandardListedPrice ?? 0,
                ProductUnit = p.Product?.Unit ?? "",
                CreatedByUserId = p.CreatedByUserId,
                CreatedByName = p.CreatedByUser?.FullName ?? "",
                ApprovedByUserId = p.ApprovedByUserId,
                ApprovedByName = p.ApprovedByUser?.FullName,
                PromptUsed = p.PromptUsed,
                TemplateName = p.TemplateName,
                Tone = p.Tone,
                Goal = p.Goal,
                GeneratedImageUrl = p.GeneratedImageUrl,
                GeneratedCaption = p.GeneratedCaption,
                SelectedImageUrl = p.SelectedImageUrl,
                EditedCaption = p.EditedCaption,
                Hashtags = p.Hashtags,
                CtaText = p.CtaText,
                Status = p.Status.ToString(),
                RejectionReason = p.RejectionReason,
                ScheduledTime = p.ScheduledTime,
                PublishedAt = p.PublishedAt,
                ExternalPostId = p.ExternalPostId,
                PublishErrorMessage = p.PublishErrorMessage,
                ReachCount = p.ReachCount,
                LikeCount = p.LikeCount,
                CommentCount = p.CommentCount,
                ShareCount = p.ShareCount,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            };
        }
    }
}
