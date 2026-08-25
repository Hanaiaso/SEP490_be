using System.ComponentModel.DataAnnotations;
using VietTien.API.Infrastructure.Validation;

namespace VietTien.API.DTOs.Delivery
{
    // ─── BƯỚC 1: LẬP LỊCH XE ─────────────────────────────────────────────────
    public class ScheduleDeliveryRequestDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Mã xe không hợp lệ.")]
        public int VehicleId { get; set; }        // Xe 1-5

        [Required(ErrorMessage = "Ca giao hàng là bắt buộc.")]
        [RegularExpression("^(Sáng|Trưa|Chiều)$", ErrorMessage = "Ca giao hàng không hợp lệ. Chọn: Sáng / Trưa / Chiều.")]
        public string Shift { get; set; } = string.Empty;  // Sáng / Trưa / Chiều

        public DateTime? DeliveryDate { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách đơn hàng không được rỗng.")]
        public List<Guid> OrderIds { get; set; } = new();
    }

    public class ScheduleDeliveryResponseDto
    {
        public int VehicleId { get; set; }
        public string Shift { get; set; } = string.Empty;
        public DateTime? DeliveryDate { get; set; }
        public int OrdersScheduled { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal TotalWeightKg { get; set; }
        public decimal? VehicleCapacityKg { get; set; }
    }

    // ─── UC-34: XUNG ĐỘT LỊCH XE/CA ─────────────────────────────────────────
    public class DeliveryScheduleConflictDto
    {
        public Guid Id { get; set; }
        public int VehicleId { get; set; }
        public string Shift { get; set; } = string.Empty;
        public DateTime RequestedDate { get; set; }
        public List<string> OrderCodes { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public Guid RaisedByUserId { get; set; }
        public string? RaisedByUserName { get; set; }
        public DateTime RaisedAt { get; set; }
        public Guid? ResolvedByUserId { get; set; }
        public string? ResolvedByUserName { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionAction { get; set; }
        public string? ResolutionNote { get; set; }
    }

    public class ResolveDeliveryConflictRequestDto
    {
        [Required(ErrorMessage = "Vui lòng chọn hành động xử lý.")]
        [RegularExpression("^(Reassign|Override|Reject)$", ErrorMessage = "Hành động không hợp lệ. Chọn: Reassign / Override / Reject.")]
        public string Action { get; set; } = string.Empty;

        // Bắt buộc khi Action = Reassign — lịch mới thay cho lịch bị trùng.
        public int? NewVehicleId { get; set; }
        public string? NewShift { get; set; }
        public DateTime? NewDate { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập ghi chú xử lý.")]
        [MaxLength(1000)]
        public string Note { get; set; } = string.Empty;
    }

    // ─── BƯỚC 1: DANH SÁCH ĐƠN GIAO HÀNG ────────────────────────────────────
    public class DeliveryOrderListDto
    {
        public Guid Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public decimal FinalPayment { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string DeliveryStatus { get; set; } = string.Empty;
        public int? VehicleId { get; set; }
        public string? Shift { get; set; }
        public DateTime? ScheduledDeliveryDate { get; set; }
        public int FailedDeliveryCount { get; set; }
        public bool IsBlocked { get; set; }         // >= 3 lần thất bại
        public int ItemCount { get; set; }
        public decimal? TotalPackedWeightKg { get; set; }
    }

    /// <summary>Số lượng việc đang chờ xử lý cho từng mục "Giao hàng" ở sidebar Sales — cho
    /// Sale thấy trực quan chỗ nào có việc cần làm mà không phải mở từng trang. Tính riêng theo
    /// từng Sales Staff (khách hàng do chính họ phụ trách), trừ ArrangementPending (điều chuyển
    /// nội bộ + thu hồi) và PendingHandover dùng chung hàng đợi toàn hệ thống như trang gốc.</summary>
    public class SalesDeliverySidebarCountsDto
    {
        public int PendingHandover { get; set; }
        public int WarehouseCoordPending { get; set; }
        public int TripsPending { get; set; }
        public int ArrangementPending { get; set; }
        public int CollectionPending { get; set; }
    }

    // ─── BƯỚC 2: GHI NHẬN KẾT QUẢ GIAO HÀNG (POD + COD) ────────────────────
    public class RecordDeliveryResultDto
    {
        /// <summary>Chữ ký số của khách hàng (base64 PNG)</summary>
        public string? CustomerSignatureBase64 { get; set; }

        /// <summary>Ảnh hiện trường giao hàng (base64)</summary>
        public string? DeliveryPhotoBase64 { get; set; }

        /// <summary>Số tiền thực thu từ khách</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Số tiền thu phải lớn hơn hoặc bằng 0")]
        public decimal AmountCollected { get; set; }

        /// <summary>delivered / failed / partially_delivered</summary>
        [Required]
        [RegularExpression("^(delivered|failed|partially_delivered)$", ErrorMessage = "Kết quả giao hàng không hợp lệ.")]
        public string DeliveryOutcome { get; set; } = "delivered";

        [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
        public string? Notes { get; set; }

        /// <summary>Khách từ chối nhận hàng (khác với giao thất bại vì lý do khác như không có người nhận)</summary>
        public bool CustomerRejected { get; set; }

        /// <summary>Mã lý do - bắt buộc khi CustomerRejected = true (vd: WRONG_ITEM, DAMAGED, CHANGED_MIND...)</summary>
        [MaxLength(50, ErrorMessage = "Mã lý do không được vượt quá 50 ký tự.")]
        public string? RejectionReasonCode { get; set; }
    }

    public class DeliveryResultResponseDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string NewDeliveryStatus { get; set; } = string.Empty;
        public string NewOrderStatus { get; set; } = string.Empty;
        public bool DebtRecordCreated { get; set; }
        public decimal? RemainingDebt { get; set; }
        public bool IsBlockedByFailures { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>Đơn vẫn được ghi nhận giao thành công dù ảnh chữ ký/hiện trường lỗi upload — cờ này báo cho Sales biết để xử lý lại bằng chứng.</summary>
        public bool SignatureUploadFailed { get; set; }
        public bool PhotoUploadFailed { get; set; }
    }

    // ─── BƯỚC 3: HỦY ĐƠN PAID (CR-06) ───────────────────────────────────────
    public class CancelPaidOrderRequestDto
    {
        [Required(ErrorMessage = "Lý do hủy đơn là bắt buộc.")]
        [MaxLength(1000, ErrorMessage = "Lý do không được vượt quá 1000 ký tự.")]
        public string Reason { get; set; } = string.Empty;
    }

    // ─── BƯỚC 4: TẠO ĐƠN THAY THẾ + CREDIT ──────────────────────────────────
    public class CreateReplacementOrderDto
    {
        [Required]
        public Guid OriginalOrderId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Đơn hàng thay thế phải có ít nhất 1 sản phẩm.")]
        public List<ReplacementOrderItemDto> Items { get; set; } = new();

        [MaxLength(200)]
        public string? CustomerName { get; set; }

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? PhoneNumber { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }
    }

    public class ReplacementOrderItemDto
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Đơn giá phải lớn hơn 0")]
        public decimal Price { get; set; }
    }

    public class ReplacementOrderResponseDto
    {
        public Guid ReplacementOrderId { get; set; }
        public string ReplacementOrderCode { get; set; } = string.Empty;
        public decimal NewOrderValue { get; set; }
        public decimal OriginalPaidAmount { get; set; }
        public decimal CreditAllocated { get; set; }      // tiền chuyển sang Credit ví
        public decimal ReallocatedAmount { get; set; }    // tiền chuyển sang đơn mới
        public decimal CustomerCreditBalance { get; set; } // số dư Credit sau giao dịch
        public string Message { get; set; } = string.Empty;
    }

    // ─── BƯỚC 5: QUARANTINE ──────────────────────────────────────────────────
    public class QuarantineReceiveDto
    {
        [Required]
        public Guid OrderId { get; set; }

        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Lý do là bắt buộc.")]
        [MaxLength(1000, ErrorMessage = "Lý do không được vượt quá 1000 ký tự.")]
        public string Reason { get; set; } = string.Empty; // Mô tả lý do trả hàng
    }

    public class QuarantineDispatchDto
    {
        /// <summary>available / damaged</summary>
        [Required]
        [RegularExpression("^(available|damaged)$", ErrorMessage = "Hành động không hợp lệ. Chọn: available / damaged.")]
        public string Action { get; set; } = "available";

        [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
        public string? Notes { get; set; }

        /// <summary>Số lượng xử lý trong lần duyệt này. Bỏ trống = xử lý toàn bộ số lượng còn lại của lô (hành vi cũ).
        /// Nếu nhỏ hơn số lượng còn lại của lô -> duyệt từng phần, phần còn lại vẫn ở trạng thái Chờ xử lý.</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng xử lý phải lớn hơn 0.")]
        public int? Quantity { get; set; }
    }

    public class QuarantineListItemDto
    {
        public Guid Id { get; set; }
        public string QuarantineCode { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
        // Phân biệt "cần kiểm tra CL" (hàng nhập từ GoodsReceipt) với "cách ly hàng khách trả"
        // (gắn với OrderId) — WarehouseDashboardService dùng đúng 2 field GoodsReceiptItemId/OrderId
        // này để tính KPI QualityCheckPending/ReturnQuarantinePending, nhưng DTO trước đây không trả
        // GoodsReceiptItemId nên trang chi tiết không lọc được, luôn ra danh sách rỗng.
        public Guid? GoodsReceiptItemId { get; set; }
        // "CustomerReturn" (khách trả hàng, gắn OrderId) hoặc "GoodsReceiptQc" (hàng lỗi/thừa/sai
        // phát hiện lúc nhập kho từ PO, gắn GoodsReceiptItemId) — FE dùng để gắn nhãn phân biệt 2
        // nguồn cách ly trên cùng 1 danh sách thay vì suy luận từ OrderCode rỗng.
        public string Source { get; set; } = string.Empty;
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string ItemSku { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // waiting / approved_available / approved_damaged
        public string ReceivedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? DispatchedAction { get; set; }
        public string? DispatchedByName { get; set; }
        public DateTime? DispatchedAt { get; set; }
    }

    // ─── BƯỚC 5: ĐIỀU XE THU HỒI HÀNG (PICKUP) ─────────────────────────────────
    public class SchedulePickupRequestDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Mã xe không hợp lệ.")]
        public int VehicleId { get; set; }

        [Required(ErrorMessage = "Ca thu hồi là bắt buộc.")]
        [RegularExpression("^(Sáng|Trưa|Chiều)$", ErrorMessage = "Ca thu hồi không hợp lệ. Chọn: Sáng / Trưa / Chiều.")]
        public string Shift { get; set; } = string.Empty;

        [NotInPast(ErrorMessage = "Không thể lên lịch thu hồi cho ngày trong quá khứ.")]
        public DateTime PickupDate { get; set; }
    }

    public class PendingPickupDto
    {
        public Guid RequestId { get; set; }
        public string RequestCode { get; set; } = string.Empty;
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string PickupStatus { get; set; } = string.Empty;
        public int? PickupVehicleId { get; set; }
        public string? PickupShift { get; set; }
        public DateTime? ScheduledPickupDate { get; set; }
        public List<string> ReturnProductNames { get; set; } = new();
        public decimal TotalWeightKg { get; set; }
        public List<PendingPickupItemDto> Items { get; set; } = new();
    }

    public class PendingPickupItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal? WeightKg { get; set; }
    }

    // ─── P2-6: Sales Manager xử lý đơn bị khóa & công nợ COD (UC-35) ──────────
    public class BlockedOrderDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public int FailedDeliveryCount { get; set; }
        public string? DeliveryRejectionReasonCode { get; set; }
        public string? AssignedSalesStaffName { get; set; }
        public decimal FinalPayment { get; set; }
    }

    public class UnblockOrderRequest
    {
        [Required(ErrorMessage = "Lý do mở khóa là bắt buộc.")]
        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }

    public class CustomerDebtManagementDto
    {
        public Guid Id { get; set; }
        public Guid CustomerProfileId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal DebtAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public int OverdueDays { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SettledAt { get; set; }
        public string? SettledByName { get; set; }
        public string? SettlementNote { get; set; }
    }

    public class SettleDebtRequest
    {
        [MaxLength(1000)]
        public string? Note { get; set; }
    }

    // ─── NHÓM C: DeliveryTrip (luồng chuyến giao Trip-based, song song với luồng theo Order) ──
    public class CreateDeliveryTripRequestDto
    {
        [Required(ErrorMessage = "Xe giao hàng là bắt buộc.")]
        public Guid VehicleId { get; set; }

        [Required(ErrorMessage = "Ca giao hàng là bắt buộc.")]
        [RegularExpression("^(Sáng|Trưa|Chiều)$", ErrorMessage = "Ca giao hàng không hợp lệ. Chọn: Sáng / Trưa / Chiều.")]
        public string Shift { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ngày giao hàng là bắt buộc.")]
        public DateTime TripDate { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách đơn hàng không được rỗng.")]
        public List<Guid> OrderIds { get; set; } = new();
    }

    public class DeliveryTripResponseDto
    {
        public Guid Id { get; set; }
        public Guid VehicleId { get; set; }
        public int VehicleNumber { get; set; }
        public string Shift { get; set; } = string.Empty;
        public DateTime TripDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? PlannedDepartureAt { get; set; }
        public DateTime? PlannedArrivalAt { get; set; }
        public decimal TotalWeightKg { get; set; }
        public decimal? VehicleCapacityKg { get; set; }
        public decimal? RemainingCapacityKg { get; set; }
        public List<Guid> OrderIds { get; set; } = new();
        public List<string> OrderCodes { get; set; } = new();
    }

    public class StartLoadingRequestDto
    {
        public DateTime? PlannedDepartureAt { get; set; }
        public DateTime? PlannedArrivalAt { get; set; }
    }

    public class AddOrdersToTripRequestDto
    {
        [Required]
        [MinLength(1, ErrorMessage = "Danh sách đơn hàng không được rỗng.")]
        public List<Guid> OrderIds { get; set; } = new();
    }

    public class RecordDeliveryAttemptRequestDto
    {
        [Required(ErrorMessage = "Đơn hàng là bắt buộc.")]
        public Guid OrderId { get; set; }

        /// <summary>Delivered / Failed</summary>
        [Required(ErrorMessage = "Kết quả giao hàng là bắt buộc.")]
        [RegularExpression("^(Delivered|Failed)$", ErrorMessage = "Kết quả giao hàng không hợp lệ. Chọn: Delivered / Failed.")]
        public string Outcome { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }
        public string? SignatureUrl { get; set; }

        [MaxLength(500, ErrorMessage = "Lý do thất bại không được vượt quá 500 ký tự.")]
        public string? FailureReason { get; set; }
    }

    public class RecordDeliveryAttemptResponseDto
    {
        public Guid OrderId { get; set; }
        public int AttemptNumber { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string NewDeliveryStatus { get; set; } = string.Empty;
        public int FailedDeliveryCount { get; set; }
        public bool IsBlockedForDelivery { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class RecordCollectionRequestDto
    {
        [Required(ErrorMessage = "Đơn hàng là bắt buộc.")]
        public Guid OrderId { get; set; }

        // Không dùng [Range] ở đây: âm hay vượt quá số còn nợ đều phải trả cùng 1 mã lỗi
        // COD_AMOUNT_INVALID (service kiểm tra cả 2 điều kiện, vì "vượt quá remaining" chỉ biết được
        // sau khi đọc Order từ DB, không thể validate bằng DataAnnotation ở tầng DTO).
        public decimal AmountCollected { get; set; }
    }

    public class RecordCollectionResponseDto
    {
        public Guid OrderId { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal RemainingDebt { get; set; }
        public bool DebtRecordCreated { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
