namespace VietTien.API.Models
{
    public class Warehouse
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // e.g., WH-PROD, WH-TRADE, WH-PE

        public ICollection<WarehouseLocation> Locations { get; set; } = new List<WarehouseLocation>();
    }
}
