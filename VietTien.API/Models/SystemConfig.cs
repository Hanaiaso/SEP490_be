namespace VietTien.API.Models
{
    public enum SystemConfigValueType
    {
        String,
        Int,
        Decimal,
        Bool
    }

    // Bảng đăng ký (registry) các tham số cấu hình hệ thống — xem business.md §7.
    // Giá trị hiệu lực thực tế nằm ở SystemConfigVersion, KHÔNG cache tại đây để tránh lệch dữ liệu.
    public class SystemConfig
    {
        public string Key { get; set; } = string.Empty; // PRIMARY KEY, vd: "COD_RESERVATION_MINUTES"
        public SystemConfigValueType ValueType { get; set; } = SystemConfigValueType.String;
        public string? Description { get; set; }
        public string? Unit { get; set; } // "Phút", "Giờ", "VND", "Lần"...
        public string OwnerLevel { get; set; } = "Admin"; // Thông tin cấp sở hữu cấu hình, vd "Admin", "Admin/CEO"
        public bool IsActive { get; set; } = true;

        public ICollection<SystemConfigVersion> Versions { get; set; } = new List<SystemConfigVersion>();
    }
}
