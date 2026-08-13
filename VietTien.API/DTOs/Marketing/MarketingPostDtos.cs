using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Marketing
{
    public class MarketingOptionDto
    {
        public int Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string Hashtags { get; set; } = string.Empty;
        public string CtaText { get; set; } = string.Empty;
    }

    public class GenerateAiContentRequestDto
    {
        [Required]
        public Guid ProductId { get; set; }

        [MaxLength(2000, ErrorMessage = "Prompt không được vượt quá 2000 ký tự.")]
        public string? Prompt { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? TemplateName { get; set; } // "Khuyến mãi", "Bán hàng B2B", "Ra mắt sản phẩm"

        [MaxLength(100)]
        public string? Tone { get; set; } // "Chuyên nghiệp", "Hào hứng", "Thân thiện"

        [MaxLength(500)]
        public string? Goal { get; set; }
    }

    public class GenerateImageRequestDto
    {
        public string? Prompt { get; set; }
    }

    public class GenerateAiContentResponseDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductSku { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }
        public string ProductUnit { get; set; } = string.Empty;
        public List<MarketingOptionDto> Options { get; set; } = new();
    }

    public class CreateMarketingPostDto
    {
        public Guid ProductId { get; set; }
        public string PromptUsed { get; set; } = string.Empty;
        public string? TemplateName { get; set; }
        public string? Tone { get; set; }
        public string? Goal { get; set; }

        public string GeneratedImageUrl { get; set; } = string.Empty;
        public string GeneratedCaption { get; set; } = string.Empty;

        public string SelectedImageUrl { get; set; } = string.Empty;
        public string EditedCaption { get; set; } = string.Empty;
        public string? Hashtags { get; set; }
        public string? CtaText { get; set; }

        public bool SubmitImmediately { get; set; } = false;
    }

    public class UpdateMarketingPostDto
    {
        public string SelectedImageUrl { get; set; } = string.Empty;
        public string EditedCaption { get; set; } = string.Empty;
        public string? Hashtags { get; set; }
        public string? CtaText { get; set; }
        public bool SubmitImmediately { get; set; } = false;
    }

    public class MarketingPostDecisionDto : IValidatableObject
    {
        [Required(ErrorMessage = "Hành động quyết định là bắt buộc.")]
        [RegularExpression("(?i)^(Approve|ApproveNow|Rework|Reject)$",
            ErrorMessage = "Hành động không hợp lệ. Chọn: Approve / ApproveNow / Rework / Reject.")]
        public string Action { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? RejectionReason { get; set; }

        public DateTime? ScheduledTime { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ScheduledTime.HasValue && ScheduledTime.Value < DateTime.UtcNow)
            {
                yield return new ValidationResult(
                    "Thời gian lên lịch không được ở trong quá khứ.",
                    new[] { nameof(ScheduledTime) });
            }
        }
    }

    public class MakeWebhookCallbackDto
    {
        public string Status { get; set; } = string.Empty; // "Success" or "Failed"
        public string? ExternalPostId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // L3-MKT-11 (hướng Make.com): Make.com tự tra Facebook Graph API bằng kết nối riêng của nó, gửi
    // số liệu về đây định kỳ — thay cho việc backend tự gọi Facebook trực tiếp.
    public class MarketingMetricsCallbackDto
    {
        public int ReachCount { get; set; }
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int ShareCount { get; set; }
    }

    public class MarketingPostForSyncDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string ExternalPostId { get; set; } = string.Empty;
    }

    // L3-MKT-11: dữ liệu reach/reaction/comment/share thật của bài đã publish, đọc từ chính các cột
    // đã có sẵn trên MarketingPost (ReachCount/LikeCount/CommentCount/ShareCount) — trước đây có cột
    // nhưng không có endpoint nào chiếu ra. Được ghi bởi Make.com qua POST .../metrics-callback.
    public class MarketingPostMetricsDto
    {
        public Guid PostId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ExternalPostId { get; set; }
        public int ReachCount { get; set; }
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int ShareCount { get; set; }
        public DateTime? LastSyncedAt { get; set; }
    }

    public class MarketingPostDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;

        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductSku { get; set; } = string.Empty;
        public string ProductImageUrl { get; set; } = string.Empty;
        public decimal ProductPrice { get; set; }
        public string ProductUnit { get; set; } = string.Empty;

        public Guid CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = string.Empty;

        public Guid? ApprovedByUserId { get; set; }
        public string? ApprovedByName { get; set; }

        public string PromptUsed { get; set; } = string.Empty;
        public string? TemplateName { get; set; }
        public string? Tone { get; set; }
        public string? Goal { get; set; }

        public string GeneratedImageUrl { get; set; } = string.Empty;
        public string GeneratedCaption { get; set; } = string.Empty;

        public string SelectedImageUrl { get; set; } = string.Empty;
        public string EditedCaption { get; set; } = string.Empty;
        public string? Hashtags { get; set; }
        public string? CtaText { get; set; }

        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }

        public DateTime? ScheduledTime { get; set; }
        public DateTime? PublishedAt { get; set; }

        public string? ExternalPostId { get; set; }
        public string? PublishErrorMessage { get; set; }

        public int ReachCount { get; set; }
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int ShareCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
