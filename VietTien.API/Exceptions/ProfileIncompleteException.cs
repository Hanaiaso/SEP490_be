namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi khách hàng cố thực hiện thao tác mua hàng (vd: thêm vào giỏ)
    /// nhưng hồ sơ chưa đầy đủ (chưa có địa chỉ giao hàng).
    /// Controller sẽ bắt riêng exception này và trả mã "PROFILE_INCOMPLETE" cho FE.
    /// </summary>
    public class ProfileIncompleteException : Exception
    {
        public ProfileIncompleteException(string message) : base(message)
        {
        }
    }
}
