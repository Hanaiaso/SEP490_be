namespace VietTien.API.Models
{
    public class Material
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty; // Jumbo, Lõi giấy, Màng co
        public string Unit { get; set; } = string.Empty; // Cây, Cái, Cuộn
        public double CurrentStock { get; set; }
        public double SafetyThreshold { get; set; } // Ngưỡng an toàn Admin cấu hình
        public DateTime? LastAlertSentDate { get; set; } // Cơ chế tự động nhắc nhở mỗi 2 ngày

        // Phương thức kiểm tra điều kiện chạm ngưỡng
        public bool IsBelowSafetyThreshold() => CurrentStock <= SafetyThreshold;

        // Navigation Properties
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    }
}
