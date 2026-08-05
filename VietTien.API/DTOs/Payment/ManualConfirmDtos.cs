using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Payment
{
    /// <summary>
    /// Request body cho POST /api/orders/{orderId}/manual-confirm
    /// Sales Manager xác nhận thanh toán SePay thủ công
    /// </summary>
    public sealed record ManualConfirmPaymentRequest
    {
        /// <summary>Mã giao dịch từ ngân hàng hoặc SePay (bắt buộc, phải unique)</summary>
        [Required(ErrorMessage = "Mã giao dịch là bắt buộc.")]
        [MaxLength(200)]
        public string ExternalTransactionId { get; init; } = string.Empty;

        /// <summary>Số tiền công ty thực nhận (phải khớp với FinalPayment của đơn hàng)</summary>
        [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền thực nhận phải lớn hơn 0")]
        public decimal ActualAmount { get; init; }

        /// <summary>URL bằng chứng đối soát đã upload (bắt buộc)</summary>
        [Required(ErrorMessage = "URL bằng chứng đối soát là bắt buộc.")]
        [MaxLength(1000)]
        public string EvidenceUrl { get; init; } = string.Empty;

        /// <summary>Nội dung chuyển khoản thực tế (để audit)</summary>
        [MaxLength(500)]
        public string? TransferContent { get; init; }

        /// <summary>Ghi chú đối soát của Sales Manager</summary>
        [MaxLength(1000)]
        public string? Note { get; init; }
    }

    /// <summary>
    /// Response sau manual-confirm — luôn trả 200 kể cả khi allocation thất bại
    /// </summary>
    public sealed record ManualConfirmPaymentResponse
    {
        public Guid OrderId { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public string ExternalTransactionId { get; init; } = string.Empty;

        /// <summary>"Paid"</summary>
        public string PaymentStatus { get; init; } = string.Empty;

        /// <summary>"Confirmed" hoặc "PaidReviewRequired"</summary>
        public string OrderStatus { get; init; } = string.Empty;

        /// <summary>"ALLOCATED" hoặc "FAILED"</summary>
        public string AllocationStatus { get; init; } = string.Empty;

        /// <summary>Mã lỗi nếu allocation thất bại (null nếu thành công)</summary>
        public string? ExceptionCode { get; init; }

        public string Message { get; init; } = string.Empty;
        public DateTime ConfirmedAt { get; init; }
    }

    /// <summary>Request body cho POST /api/orders/{orderId}/retry-allocation</summary>
    public sealed record RetryAllocationRequest
    {
        public string? Note { get; init; }
    }

    /// <summary>Item trong danh sách ngoại lệ MGR-05</summary>
    public sealed record SePayExceptionItemDto
    {
        public Guid OrderId { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public string? AssignedSalesStaffName { get; init; }
        public decimal FinalPayment { get; init; }
        public string PaymentStatus { get; init; } = string.Empty;
        public string OrderStatus { get; init; } = string.Empty;
        public string ExceptionReason { get; init; } = string.Empty;
        public int RetryCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastRetryAt { get; init; }

        /// <summary>Thông tin transaction manual confirm (nếu có)</summary>
        public string? ExternalTransactionId { get; init; }
        public string? EvidenceUrl { get; init; }
    }
}
