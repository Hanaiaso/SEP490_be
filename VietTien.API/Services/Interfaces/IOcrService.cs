namespace VietTien.API.Services.Interfaces
{
    public interface IOcrService
    {
        Task<InvoiceOcrResult> ExtractInvoiceAsync(Stream fileStream, string? contentType);
    }

    public class InvoiceOcrResult
    {
        public string? VendorName { get; set; }
        public DateTime? DueDate { get; set; }
        public List<InvoiceOcrItem> Items { get; set; } = new();
    }

    public class InvoiceOcrItem
    {
        public string Description { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
    }
}
