namespace VietTien.API.Models
{
    // Lịch sử phiên bản bất biến của 1 SystemConfig. Giá trị hiệu lực tại một thời điểm
    // = bản ghi có EffectiveDate <= thời điểm đó, mới nhất theo EffectiveDate rồi tới CreatedAt.
    public class SystemConfigVersion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string ConfigKey { get; set; } = string.Empty;
        public SystemConfig? Config { get; set; }

        public string Value { get; set; } = string.Empty;
        public DateTime EffectiveDate { get; set; }

        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ChangeReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
