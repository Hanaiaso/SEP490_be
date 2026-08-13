namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi file media upload vượt giới hạn kích thước cho phép — L3-MKT-10. Controller bắt
    /// riêng exception này để trả 413 Payload Too Large.
    /// </summary>
    public class MediaTooLargeException : Exception
    {
        public MediaTooLargeException(string message) : base(message)
        {
        }
    }
}
