namespace VietTien.API.Models
{
    public class Material
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty; // Jumbo, Lõi giấy, Màng co
        public string Unit { get; set; } = string.Empty; // Cây, Cái, Cuộn
        public double CurrentStock { get; set; }
        public double SafetyThreshold { get; set; } // Ngưỡng an toàn Admin cấu hình
        public double? MaxStockThreshold { get; set; } // Ngưỡng tồn đọng (null = chưa cấu hình, không cảnh báo)
        public DateTime? LastAlertSentDate { get; set; } // Cơ chế tự động nhắc nhở mỗi 2 ngày (dùng chung cho cả cảnh báo thấp và tồn đọng)

        // Phương thức kiểm tra điều kiện chạm ngưỡng
        public bool IsBelowSafetyThreshold() => CurrentStock <= SafetyThreshold;

        // Navigation Properties
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    }
}
