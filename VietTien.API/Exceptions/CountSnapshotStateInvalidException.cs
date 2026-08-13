namespace VietTien.API.Exceptions
{
    /// <summary>Ném ra khi khóa snapshot lý thuyết cho 1 InventoryCountSession không còn ở trạng thái Draft (đã khóa hoặc đã hoàn tất).</summary>
    public class CountSnapshotStateInvalidException : Exception
    {
        public CountSnapshotStateInvalidException(string message) : base(message) { }
    }
}
