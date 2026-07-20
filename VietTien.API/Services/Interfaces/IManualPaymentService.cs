using VietTien.API.DTOs.Payment;

namespace VietTien.API.Services.Interfaces
{
    /// <summary>
    /// Service xử lý ngoại lệ thanh toán SePay — MGR-05
    /// </summary>
    public interface IManualPaymentService
    {
        /// <summary>
        /// Sales Manager xác nhận thanh toán SePay thủ công.
        /// - Validate: method=SePay, evidence, amount, unique transactionId
        /// - Cập nhật PaymentStatus = Paid
        /// - Thử phân bổ tồn kho
        /// - Nếu đủ tồn: OrderStatus = Confirmed + tạo PickTask
        /// - Nếu thiếu tồn: OrderStatus = PaidReviewRequired + tạo PaymentException
        /// </summary>
        Task<ManualConfirmPaymentResponse> ManualConfirmAsync(
            Guid orderId,
            ManualConfirmPaymentRequest request,
            Guid confirmedByUserId,
            CancellationToken ct = default);

        /// <summary>
        /// Sales Manager thử phân bổ lại tồn kho sau khi tiền đã PAID.
        /// Điều kiện: PaymentStatus = Paid, OrderStatus = PaidReviewRequired
        /// </summary>
        Task<ManualConfirmPaymentResponse> RetryAllocationAsync(
            Guid orderId,
            Guid retryByUserId,
            string? note,
            CancellationToken ct = default);

        /// <summary>
        /// Lấy danh sách ngoại lệ hiển thị trên màn hình MGR-05:
        /// - PaymentStatus = Pending và đơn cũ hơn 30 phút
        /// - PaymentStatus = Paid + OrderStatus = PaidReviewRequired
        /// </summary>
        Task<List<SePayExceptionItemDto>> GetExceptionsAsync(CancellationToken ct = default);
    }
}
