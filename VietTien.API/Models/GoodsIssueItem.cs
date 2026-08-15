namespace VietTien.API.Models
{
    public class GoodsIssueItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoodsIssueId { get; set; }

        // Chỉ 1 trong 2 được fill: ProductId (xuất thành phẩm) hoặc MaterialId (xuất nguyên liệu)
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        public int Quantity { get; set; }

        public string? Note { get; set; }

        // Navigation
        public GoodsIssue GoodsIssue { get; set; } = null!;
        public Product? Product { get; set; }
        public Material? Material { get; set; }
    }
}
