namespace VietTien.API.Models
{
    public class PickTaskItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PickTaskId { get; set; }
        public Guid ProductId { get; set; }

        public int QuantityToPick { get; set; }
        public int PickedQuantity { get; set; }

        // Navigation
        public PickTask PickTask { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
