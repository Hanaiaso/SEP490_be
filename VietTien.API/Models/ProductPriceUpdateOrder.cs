using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    // Luồng cập nhật giá hàng hóa có kiểm soát: CEO đề xuất (nhiều sản phẩm/đợt) -> Sales Manager
    // phân công 1 Sales Staff cụ thể + thông báo khách hàng bị ảnh hưởng -> Sales Staff thực hiện
    // đúng ngày hiệu lực. Thay thế việc CEO sửa Product.StandardListedPrice trực tiếp, tức thời.
    public class ProductPriceUpdateOrder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ProductPriceUpdateOrderStatus Status { get; set; } = ProductPriceUpdateOrderStatus.Proposed;

        public Guid ProposedByUserId { get; set; }
        public User ProposedByUser { get; set; } = null!;
        public DateTime ProposedAt { get; set; } = DateTime.UtcNow;
        public string? ProposalNote { get; set; }

        // Do CEO đặt lúc đề xuất, không đổi sau đó -> mốc so sánh đúng/trễ hạn không bị mập mờ.
        public DateTime ScheduledEffectiveDate { get; set; }

        public Guid? AssignedByManagerId { get; set; }
        public User? AssignedByManager { get; set; }
        public Guid? AssignedSalesStaffId { get; set; }
        public User? AssignedSalesStaff { get; set; }
        public DateTime? NotifiedAt { get; set; }

        public Guid? ExecutedByUserId { get; set; }
        public User? ExecutedByUser { get; set; }
        public DateTime? ExecutedAt { get; set; }

        public Guid? CancelledByUserId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }

        // Concurrency token: chặn double-click Execute / Execute đua với Cancel muộn.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<ProductPriceUpdateOrderItem> Items { get; set; } = new List<ProductPriceUpdateOrderItem>();
    }

    public enum ProductPriceUpdateOrderStatus
    {
        Proposed,
        Notified,
        Executed,
        Cancelled
    }

    public class ProductPriceUpdateOrderItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductPriceUpdateOrderId { get; set; }
        public ProductPriceUpdateOrder ProductPriceUpdateOrder { get; set; } = null!;
        public Guid ProductId { get; set; }
        public Product Product { get; set; } = null!;

        // Snapshot StandardListedPrice tại thời điểm đề xuất -> ExecuteAsync re-check giá chưa bị
        // 1 tác vụ khác đổi từ lúc đề xuất trước khi ghi đè.
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
    }
}
