using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum PickTaskStatus { Pending, Picking, Completed, Exception }

    public class PickTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public Guid? AssignedUserId { get; set; }

        public PickTaskStatus Status { get; set; } = PickTaskStatus.Pending;
        
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
