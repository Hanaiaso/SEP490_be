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
    }

    public class QuarantineListItemDto
    {
        public Guid Id { get; set; }
        public string QuarantineCode { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
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
        public List<PendingPickupItemDto> Items { get; set; } = new();
    }

    public class PendingPickupItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
