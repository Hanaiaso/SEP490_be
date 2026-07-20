namespace VietTien.API.DTOs.SePay
{
    public class SePayWebhookDto
    {
        public int id { get; set; }
        public string gateway { get; set; } = string.Empty;
        public string transactionDate { get; set; } = string.Empty;
        public string accountNumber { get; set; } = string.Empty;
        public string? subAccount { get; set; }
        public decimal transferAmount { get; set; }
        public string transferType { get; set; } = string.Empty;
        public string transferContent { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
        public string referenceNumber { get; set; } = string.Empty;
        public string referenceCode { get; set; } = string.Empty;
        public string? description { get; set; }
    }
}
