namespace VietTien.API.Models
{
    public class WarehouseLocation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid WarehouseId { get; set; }
        
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Normal"; // e.g., Normal, Quarantine, Damaged

        // Navigation Properties
        public Warehouse Warehouse { get; set; } = null!;
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    }
}
