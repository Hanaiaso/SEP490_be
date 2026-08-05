using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Admin
{
    public class SystemConfigDto
    {
        public string Key { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public string OwnerLevel { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? EffectiveValue { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public int VersionCount { get; set; }
    }

    public class SystemConfigVersionDto
    {
        public Guid Id { get; set; }
        public string ConfigKey { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime EffectiveDate { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ChangeReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsCurrentlyEffective { get; set; }
    }

    public class UpdateSystemConfigRequest
    {
        [Required(ErrorMessage = "Giá trị cấu hình không được để trống.")]
        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;

        public DateTime? EffectiveDate { get; set; } // null => hiệu lực ngay lập tức (UtcNow)

        [Required(ErrorMessage = "Vui lòng nhập lý do thay đổi cấu hình để phục vụ audit.")]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
