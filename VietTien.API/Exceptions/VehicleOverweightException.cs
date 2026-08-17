namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi tổng trọng lượng đơn hàng gán vào 1 DeliveryTrip vượt Vehicle.Capacity.</summary>
    public class VehicleOverweightException : Exception
    {
        public VehicleOverweightException(string message) : base(message) { }
    }
}
