using System;
using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum MarketingPostStatus
    {
        Draft,
        Submitted,
        Approved,
        Rejected,
        ReworkRequired,
        Scheduled,
        Posting,
        Success,
        PublishFailed
    }

    public class MarketingPost
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Code { get; set; } = string.Empty; // MP-202607-0001

        public Guid ProductId { get; set; }
        public Guid CreatedByUserId { get; set; }
        public Guid? ApprovedByUserId { get; set; }

        public string PromptUsed { get; set; } = string.Empty;
        public string? TemplateName { get; set; }
        public string? Tone { get; set; }
        public string? Goal { get; set; }

        // AI Candidates / Raw Output
        public string GeneratedImageUrl { get; set; } = string.Empty;
        public string GeneratedCaption { get; set; } = string.Empty;

        // Final edited fields
        public string SelectedImageUrl { get; set; } = string.Empty;
        public string EditedCaption { get; set; } = string.Empty;
        public string? Hashtags { get; set; }
        public string? CtaText { get; set; }

        public MarketingPostStatus Status { get; set; } = MarketingPostStatus.Draft;
        public string? RejectionReason { get; set; }

        public DateTime? ScheduledTime { get; set; }
        public DateTime? PublishedAt { get; set; }

        public string? ExternalPostId { get; set; } // Facebook Post ID from Make.com
        public string? PublishErrorMessage { get; set; }

        public int ReachCount { get; set; }
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int ShareCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public Product Product { get; set; } = null!;
        public User CreatedByUser { get; set; } = null!;
        public User? ApprovedByUser { get; set; }
    }
}
