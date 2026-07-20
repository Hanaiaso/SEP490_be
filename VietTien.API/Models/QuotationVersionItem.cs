namespace VietTien.API.Models
{
    public class QuotationVersionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuotationVersionId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal OriginalUnitPrice { get; set; }
        public decimal ProposedUnitPrice { get; set; }

        // Navigation
        public QuotationVersion QuotationVersion { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
