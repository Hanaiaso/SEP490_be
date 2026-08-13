namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi file media upload sai định dạng (đuôi hoặc magic byte không khớp cho phép) —
    /// L3-MKT-09. Controller bắt riêng exception này để trả 415 Unsupported Media Type.
    /// </summary>
    public class MediaTypeUnsupportedException : Exception
    {
        public MediaTypeUnsupportedException(string message) : base(message)
        {
        }
    }
}
