using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.SePay
{
    // Payload từ webhook bên thứ 3 (SePay) — không dùng [Required] cho các trường string vì
    // không kiểm soát được toàn bộ hình dạng payload thực tế của bên ngoài. Phòng thủ chính
    // là kiểm tra x-sepay-token (xem SePayController); ở đây chỉ chặn giá trị tiền vô lý.
    public class SePayWebhookDto
    {
        public int id { get; set; }
        public string gateway { get; set; } = string.Empty;
        public string transactionDate { get; set; } = string.Empty;
        public string accountNumber { get; set; } = string.Empty;
        public string? subAccount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "transferAmount phải lớn hơn hoặc bằng 0")]
        public decimal transferAmount { get; set; }

        public string transferType { get; set; } = string.Empty;
        public string transferContent { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
        public string referenceNumber { get; set; } = string.Empty;
        public string referenceCode { get; set; } = string.Empty;
        public string? description { get; set; }
    }
}
