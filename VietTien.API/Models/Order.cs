using System.ComponentModel.DataAnnotations;

namespace VietTien.API.Models
{
    public enum PaymentMethod { COD, SePay, Cash }
    public enum PaymentStatus { Unpaid, Pending, Paid, PartiallyPaid, Failed, Refunded }
    public enum OrderStatus { Draft, PendingPayment, PendingConfirmation, Confirmed, Processing, Completed, CancelRequested, CancelledReallocated, Cancelled, PaidReviewRequired, Returned }
    public enum FulfillmentStatus { Unallocated, Reserved, Allocated, Picking, PartiallyReady, Ready, Consolidating, Consolidated, HandedOver, Fulfilled }
    public enum DeliveryStatus { NotScheduled, Scheduled, InDelivery, Delivered, Failed, PartiallyDelivered, Rescheduled, Cancelled }
    public enum RedInvoiceStatus { None, Pending, Issued, SentToCustomer }
    public class Order
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomerProfileId { get; set; }
        public string OrderCode { get; set; } = string.Empty; // Mã độc nhất đối soát SePay

        // GH-02/GH-15: concurrency token — chặn 2 request cùng lúc (webhook + manual-confirm, hoặc
        // 2 lần duyệt huỷ song song) đều đọc cùng 1 trạng thái rồi cùng ghi đè nhau. Request thua sẽ
        // nhận DbUpdateConcurrencyException, middleware map sẵn 409.
        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal CreditApplied { get; set; } = 0m;
        public decimal FinalPayment { get; set; }

        public PaymentMethod PaymentMethod { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Unallocated;
        public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.NotScheduled;

        public Guid? ReplacementOrderId { get; set; }

        /// <summary>Thời điểm OrderStatus chuyển sang Confirmed (dùng tính KPI ProcessingSpeed). Null nếu đơn tạo trước khi field này tồn tại hoặc chưa từng Confirmed.</summary>
        public DateTime? ConfirmedAt { get; set; }

        // ─── MGR-05: Manual SePay confirmation fields ───
        /// <summary>Sales Manager xác nhận thanh toán thủ công</summary>
        public Guid? ManualConfirmedByUserId { get; set; }

        /// <summary>Thời điểm Sales Manager xác nhận</summary>
        public DateTime? ManualConfirmedAt { get; set; }

        /// <summary>URL bằng chứng đối soát do Sales Manager upload</summary>
        public string? ManualConfirmEvidenceUrl { get; set; }

        /// <summary>Snapshot địa chỉ giao hàng đã chọn tại thời điểm đặt hàng (bất biến).
        /// Đơn tạo trước khi có trường này (null) fallback về địa chỉ mặc định hiện tại của khách.</summary>
        public string? ShippingAddress { get; set; }

        public int? DeliveryVehicleId { get; set; } // Giới hạn từ xe 1 -> xe 5
        public string? DeliveryShift { get; set; }   // Sáng, Trưa, Chiều
        public DateTime? ScheduledDeliveryDate { get; set; }

        // ─── Nhóm C: DeliveryTrip (luồng Trip-based mới, song song với luồng theo Order ở trên) ───
        /// <summary>Đơn thuộc tối đa 1 chuyến giao đang hoạt động. Null nếu chưa được gom vào chuyến nào.</summary>
        public Guid? DeliveryTripId { get; set; }
        public DeliveryTrip? DeliveryTrip { get; set; }

        public Guid? WarehouseStaffId { get; set; } // Nhân viên kho thực hiện đơn hàng

        // Snapshot Sale phụ trách đơn tại thời điểm tạo (LUỒNG 7):
        // đơn COMPLETED giữ nguyên Sale lịch sử khi khách đổi Sale; chỉ Manager mới chuyển giá trị này khi phê duyệt
        public Guid? SalesStaffId { get; set; }
        public bool IsExternalOrder { get; set; } = false; // Nhận diện đơn mua ngoài (External Orders)

        // ─── LUỒNG 5: Giao hàng & Thu COD ───
        /// <summary>Số lần giao thất bại (>= 3 → hệ thống khóa, escalate manager)</summary>
        public int FailedDeliveryCount { get; set; } = 0;

        /// <summary>Bị khóa tự động khi FailedDeliveryCount >= 3</summary>
        public bool IsBlockedForDelivery { get; set; } = false;

        /// <summary>Số tiền thực thu từ khách (COD)</summary>
        public decimal AmountPaid { get; set; } = 0;

        /// <summary>URL ảnh chữ ký số của khách hàng (Proof of Delivery)</summary>
        public string? CustomerSignatureUrl { get; set; }

        /// <summary>URL ảnh hiện trường giao hàng (Proof of Delivery)</summary>
        public string? DeliveryPhotoUrl { get; set; }

        /// <summary>Mã lý do khi khách từ chối nhận hàng (bắt buộc khi RecordDeliveryResultDto.CustomerRejected = true)</summary>
        public string? DeliveryRejectionReasonCode { get; set; }

        /// <summary>Thời điểm giao hàng thực tế</summary>
        public DateTime? DeliveredAt { get; set; }

        /// <summary>Thời điểm bắt đầu chuẩn bị/đóng gói hàng</summary>
        public DateTime? PickingStartedAt { get; set; }

        /// <summary>Thời điểm hoàn thành chuẩn bị/đóng gói hàng</summary>
        public DateTime? PickingCompletedAt { get; set; }

        // ─── P2-6: Sales Manager mở khóa giao lại (UC-35) ───
        /// <summary>Thời điểm Sales Manager mở khóa cho đơn giao lại</summary>
        public DateTime? UnblockedAt { get; set; }
        public Guid? UnblockedByUserId { get; set; }
        public string? UnblockReason { get; set; }
        public User? UnblockedByUser { get; set; }

        // ─── LUỒNG 5: Hủy đơn PAID (CR-06) ───
        /// <summary>Lý do hủy đơn hàng</summary>
        public string? CancelReason { get; set; }

        /// <summary>Thời điểm yêu cầu hủy</summary>
        public DateTime? CancelRequestedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? InvoicePdfUrl { get; set; }

        public bool RequiresRedInvoice { get; set; } = false;
        public RedInvoiceStatus RedInvoiceStatus { get; set; } = RedInvoiceStatus.None;

        // Navigation Properties
        public CustomerProfile CustomerProfile { get; set; } = null!;
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
        public ICollection<CustomerDebt> Debts { get; set; } = new List<CustomerDebt>();
        public ICollection<ReturnedGoodsLog> ReturnedGoodsLogs { get; set; } = new List<ReturnedGoodsLog>();
        public Order? ReplacementOrder { get; set; }
        public User? ManualConfirmedBy { get; set; }
        public User? SalesStaff { get; set; }
        public ICollection<PaymentException> PaymentExceptions { get; set; } = new List<PaymentException>();
        public ICollection<ReturnExchangeRequest> ReturnExchangeRequests { get; set; } = new List<ReturnExchangeRequest>();
    }
}
