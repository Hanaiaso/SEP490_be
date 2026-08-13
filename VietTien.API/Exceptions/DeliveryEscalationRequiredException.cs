namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra ở lần giao thất bại thứ 4 trong 1 DeliveryTrip: đơn bị khóa, cần Sales Manager xử lý.</summary>
    public class DeliveryEscalationRequiredException : Exception
    {
        public DeliveryEscalationRequiredException(string message) : base(message) { }
    }
}
