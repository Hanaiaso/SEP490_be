using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Order
{
    /// <summary>
    /// L3-AS-02/AS-07: endpoint độc lập để phân bổ lại một phần giá trị đã thanh toán của đơn đã
    /// CancelRequested — khác với ApproveCancelAndCreateReplacementAsync (bundle luôn việc tạo đơn
    /// thay thế theo danh sách Items), ở đây caller chỉ định thẳng SỐ TIỀN muốn phân bổ.
    /// </summary>
    public class CreatePaymentReallocationRequestDto
    {
        [Required]
        public Guid OriginalOrderId { get; set; }

        // Nếu có: chuyển Amount sang đơn này (phải cùng khách với đơn gốc).
        // Nếu null: Amount chuyển thẳng vào CustomerProfile.AvailableCredit.
        public Guid? TargetOrderId { get; set; }

        [Range(1, double.MaxValue, ErrorMessage = "Số tiền phân bổ phải lớn hơn 0.")]
        public decimal Amount { get; set; }
    }

    public class PaymentReallocationResponseDto
    {
        public Guid Id { get; set; }
        public Guid OriginalOrderId { get; set; }
        public Guid? TargetOrderId { get; set; }
        public decimal Amount { get; set; }
        public decimal RemainingAfter { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
