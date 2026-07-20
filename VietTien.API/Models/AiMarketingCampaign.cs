namespace VietTien.API.Models
{
    public enum CampaignStatus { Draft, Scheduled, Posting, Success, Failed }
    public class AiMarketingCampaign
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Guid AdminId { get; set; }
        public string PromptUsed { get; set; } = string.Empty;
        public string GeneratedImageUrl { get; set; } = string.Empty; // Lưu vào thư viện
        public string GeneratedCaption { get; set; } = string.Empty;
        public DateTime? ScheduledTime { get; set; }
        public string? FacebookPostId { get; set; }
        public CampaignStatus Status { get; set; }

        // Mạng xã hội Insights API kết nối real-time
        public int ReachCount { get; set; }
        public int EngagementCount { get; set; }

        // Navigation Properties
        public Product Product { get; set; } = null!;
        public User Admin { get; set; } = null!;
    }
}
