namespace VietTien.API.Models
{
    public class ReturnedGoodsLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid WarehouseStaffId { get; set; }
        public DateTime InspectionDate { get; set; } = DateTime.UtcNow;
        public string Condition { get; set; } = string.Empty; // Đạt chuẩn / Lỗi hỏng chuyển kho biệt trữ
        public int QuantityReturned { get; set; }

        // Navigation Properties
        public Order Order { get; set; } = null!;
    }
}
