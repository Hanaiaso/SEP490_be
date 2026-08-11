namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi khách hàng cố đặt đơn hàng ĐẦU TIÊN nhưng số điện thoại chưa được xác thực OTP
    /// (UC-13/BR-004: FIRST_ORDER purpose). Controller bắt riêng exception này và trả mã
    /// "PHONE_OTP_REQUIRED" cho FE để mở modal xác thực OTP thay vì báo lỗi chung chung.
    /// </summary>
    public class PhoneVerificationRequiredException : Exception
    {
        public PhoneVerificationRequiredException(string message) : base(message)
        {
        }
    }
}
