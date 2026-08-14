namespace VietTien.API.Models
{
    public class ProductImage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        public Product Product { get; set; } = null!;
    }
}
