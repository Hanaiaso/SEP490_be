namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi tạo DeliveryTrip trùng xe + ca + ngày với 1 chuyến khác đang Scheduled/InDelivery.</summary>
    public class VehicleShiftConflictException : Exception
    {
        public VehicleShiftConflictException(string message) : base(message) { }
    }
}
