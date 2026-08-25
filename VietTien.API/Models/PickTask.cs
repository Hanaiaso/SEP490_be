using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum PickTaskStatus { Pending, Picking, Completed, Exception, Cancelled }

    public class PickTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public Guid? AssignedUserId { get; set; }

        public PickTaskStatus Status { get; set; } = PickTaskStatus.Pending;

        // Concurrency token: chống 2 nhân viên kho cùng "Nhận lệnh xuất kho" trong cùng khoảng ngắn
        // ghi đè AssignedUserId của nhau -> throw DbUpdateConcurrencyException, middleware map sẵn thành 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        
        [MaxLength(1000)]
        public string? ImageProofUrl { get; set; }
        public string? Note { get; set; }

        // Navigation
        public Order Order { get; set; } = null!;
        public Warehouse Warehouse { get; set; } = null!;
        public User? AssignedUser { get; set; }
        public ICollection<PickTaskItem> Items { get; set; } = new List<PickTaskItem>();
    }
}
