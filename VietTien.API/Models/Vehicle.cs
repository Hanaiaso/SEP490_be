namespace VietTien.API.Models
{
    // Master data xe điều phối giao hàng (business.md: "5 xe, 3 ca"). VehicleNumber giữ đúng
    // cách đánh số "xe 1..5" đã dùng sẵn trong Order.DeliveryVehicleId/ScheduleDeliveryRequestDto.
    public class Vehicle
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int VehicleNumber { get; set; }
        public string LicensePlate { get; set; } = string.Empty;
        public decimal? Capacity { get; set; } // kg
        public bool IsActive { get; set; } = true;
        public string? Note { get; set; }
    }
}
