namespace VietTien.API.Models
{
    public class ProductMaterial
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProductId { get; set; }
        public Product Product { get; set; } = null!;

        public Guid MaterialId { get; set; }
        public Material Material { get; set; } = null!;

        public double QuantityRequired { get; set; } // Định mức tiêu hao (ví dụ: cần 0.05 cuộn Jumbo/1 sản phẩm)
    }
}
