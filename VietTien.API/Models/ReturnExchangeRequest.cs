namespace VietTien.API.Models
{
    public enum ReturnExchangeStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,
        Cancelled = 3
    }

    public enum PickupStatus
    {
        NotScheduled = 0,
        Scheduled = 1,
        PickedUp = 2
    }


    public class ReturnExchangeRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid CustomerProfileId { get; set; }
        public Guid RequestedByUserId { get; set; } // Người tạo yêu cầu (Sale hoặc Customer)

        public ReturnExchangeStatus Status { get; set; } = ReturnExchangeStatus.Pending;
        public string Reason { get; set; } = string.Empty;
        public string? EvidenceUrls { get; set; } // JSON array Cloudinary URLs

        public string? ManagerNote { get; set; } // Ghi chú khi duyệt/từ chối
        public Guid? ProcessedByUserId { get; set; }
        public DateTime? ProcessedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Logistics
        public PickupStatus PickupStatus { get; set; } = PickupStatus.NotScheduled;
        public int? PickupVehicleId { get; set; }
        public string? PickupShift { get; set; }
        public DateTime? ScheduledPickupDate { get; set; }

        /// <summary>Snapshot địa chỉ lấy hàng, sao chép từ Order.ShippingAddress tại thời điểm tạo yêu cầu (bất biến).</summary>
        public string? PickupAddress { get; set; }
        

        // Navigation Properties
        public Order Order { get; set; } = null!;
        public Guid? ReplacementOrderId { get; set; }
        public Order? ReplacementOrder { get; set; }

        public CustomerProfile CustomerProfile { get; set; } = null!;
        
        [System.ComponentModel.DataAnnotations.Schema.ForeignKey(nameof(RequestedByUserId))]
        public User RequestedBy { get; set; } = null!;
        
        [System.ComponentModel.DataAnnotations.Schema.ForeignKey(nameof(ProcessedByUserId))]
        public User? ProcessedBy { get; set; }

        public ICollection<ReturnExchangeRequestItem> ReturnItems { get; set; } = new List<ReturnExchangeRequestItem>();
        public ICollection<ReturnExchangeRequestNewItem> ExchangeItems { get; set; } = new List<ReturnExchangeRequestNewItem>();
    }

    // Các món hàng muốn trả lại từ đơn gốc
    public class ReturnExchangeRequestItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ReturnExchangeRequestId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }

        // Mức giá lúc mua (để cấn trừ)
        public decimal PriceSnapshot { get; set; } 

        public ReturnExchangeRequest ReturnExchangeRequest { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }

    // Các món hàng muốn đổi lấy (Nếu rỗng tức là chỉ trả hàng)
    public class ReturnExchangeRequestNewItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ReturnExchangeRequestId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }

        // Giá hiện tại của sản phẩm mới
        public decimal PriceSnapshot { get; set; }

        public ReturnExchangeRequest ReturnExchangeRequest { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
