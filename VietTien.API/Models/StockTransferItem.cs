namespace VietTien.API.Models
{
    public class StockTransferItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StockTransferId { get; set; }

        // Chỉ 1 trong 2 được fill
        public Guid? ProductId { get; set; }
        public Guid? MaterialId { get; set; }

        public int Quantity { get; set; }
        public int? ReceivedQuantity { get; set; }

        // Navigation
        public StockTransfer StockTransfer { get; set; } = null!;
        public Product? Product { get; set; }
        public Material? Material { get; set; }
    }
}
