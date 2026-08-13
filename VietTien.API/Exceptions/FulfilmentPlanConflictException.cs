namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi thực thi gộp pick (multi-pick) nhưng không có MultiPickApproval nào đã Approved khớp đúng tập đơn hàng.</summary>
    public class FulfilmentPlanConflictException : Exception
    {
        public FulfilmentPlanConflictException(string message) : base(message) { }
    }
}
