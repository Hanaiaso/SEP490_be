namespace VietTien.API.Models
{
    public enum HandoverStatus { Pending, Confirmed }

    public class HandoverRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        
        public Guid? WarehouseStaffId { get; set; }
        public Guid? SalesStaffId { get; set; }

        public HandoverStatus Status { get; set; } = HandoverStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? HandoverTime { get; set; }

        // Mật khẩu OTP hoặc Digital Signature minh chứng xác nhận kép
        public string? WarehouseSignature { get; set; }
        public string? SalesSignature { get; set; }

        // Navigation
        public Order Order { get; set; } = null!;
        public User? WarehouseStaff { get; set; }
        public User? SalesStaff { get; set; }
    }
}
