namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi tạo DeliveryTrip với xe không tồn tại hoặc đã ngừng hoạt động (IsActive = false).</summary>
    public class VehicleNotAvailableException : Exception
    {
        public VehicleNotAvailableException(string message) : base(message) { }
    }
}
