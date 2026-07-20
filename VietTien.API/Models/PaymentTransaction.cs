namespace VietTien.API.Models
{
    /// <summary>Source của giao dịch thanh toán</summary>
    public enum PaymentTransactionSource
    {
        SePayWebhook,       // Tự động từ SePay webhook
        ManualConfirmation  // Sales Manager xác nhận thủ công
    }

    public class PaymentTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }

        /// <summary>Mã giao dịch từ SePay hoặc ngân hàng (unique)</summary>
        public string TransactionId { get; set; } = string.Empty;

        public decimal Amount { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty; // Nội dung CK
        public bool IsSuccess { get; set; }
        public bool IsManualConfirmed { get; set; } = false;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // ─── MGR-05: Manual Confirmation fields ───
        /// <summary>Nguồn gốc giao dịch: webhook tự động hay xác nhận thủ công</summary>
        public PaymentTransactionSource Source { get; set; } = PaymentTransactionSource.SePayWebhook;

        /// <summary>URL bằng chứng đối soát (ảnh bank statement / screenshot)</summary>
        public string? EvidenceUrl { get; set; }

        /// <summary>Sales Manager thực hiện xác nhận thủ công</summary>
        public Guid? ConfirmedByUserId { get; set; }

        /// <summary>Thời điểm Sales Manager xác nhận</summary>
        public DateTime? ConfirmedAt { get; set; }

        /// <summary>Ghi chú đối soát của Sales Manager</summary>
        public string? Note { get; set; }

        // Navigation Properties
        public Order Order { get; set; } = null!;
        public User? ConfirmedBy { get; set; }
    }
}
