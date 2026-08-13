namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi yêu cầu phân bổ (payment reallocation) vượt quá giá trị còn lại của khoản thanh
    /// toán gốc, hoặc phân bổ trùng/chồng lấp một khoản đã phân bổ trước đó, hoặc phân bổ sang đơn
    /// hàng của khách hàng khác (L3-AS-02/AS-07). Controller bắt riêng exception này để trả mã
    /// "REALLOCATION_VALUE_CONFLICT" (409).
    /// </summary>
    public class ReallocationValueConflictException : Exception
    {
        public ReallocationValueConflictException(string message) : base(message)
        {
        }
    }
}
