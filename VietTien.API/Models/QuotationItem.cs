namespace VietTien.API.Models
{
    public class QuotationItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuotationId { get; set; }
        public Guid ProductId { get; set; }
        
        public int Quantity { get; set; }
        public decimal OriginalUnitPrice { get; set; }

        // Navigation Properties
        public Quotation Quotation { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
