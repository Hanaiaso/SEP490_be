namespace VietTien.API.Models
{
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        // Navigation Property
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
