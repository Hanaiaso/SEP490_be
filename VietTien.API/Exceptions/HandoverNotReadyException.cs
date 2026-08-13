namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi bắt đầu DeliveryTrip nhưng có đơn chưa có HandoverRecord ở trạng thái Confirmed.</summary>
    public class HandoverNotReadyException : Exception
    {
        public HandoverNotReadyException(string message) : base(message) { }
    }
}
