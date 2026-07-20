namespace VietTien.API.DTOs.Delivery
{
    // ─── BƯỚC 1: LẬP LỊCH XE ─────────────────────────────────────────────────
    public class ScheduleDeliveryRequestDto
    {
        public int VehicleId { get; set; }        // Xe 1-5
        public string Shift { get; set; } = string.Empty;  // Sáng / Trưa / Chiều
        public DateTime? DeliveryDate { get; set; }
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
        public decimal AmountCollected { get; set; }

        /// <summary>delivered / failed / partially_delivered</summary>
        public string DeliveryOutcome { get; set; } = "delivered";

        public string? Notes { get; set; }
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
    }

    // ─── BƯỚC 3: HỦY ĐƠN PAID (CR-06) ───────────────────────────────────────
    public class CancelPaidOrderRequestDto
    {
        public string Reason { get; set; } = string.Empty;
    }

    // ─── BƯỚC 4: TẠO ĐƠN THAY THẾ + CREDIT ──────────────────────────────────
    public class CreateReplacementOrderDto
    {
        public Guid OriginalOrderId { get; set; }
        public List<ReplacementOrderItemDto> Items { get; set; } = new();
        public string? CustomerName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
    }

    public class ReplacementOrderItemDto
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
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
        public Guid OrderId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public string Reason { get; set; } = string.Empty; // Mô tả lý do trả hàng
    }

    public class QuarantineDispatchDto
    {
        /// <summary>available / damaged</summary>
        public string Action { get; set; } = "available";
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
}
