namespace VietTien.API.Exceptions
{
    /// <summary>
    /// Ném ra khi lập lịch xe/ca bị trùng với chuyến khác cùng xe + ca + ngày (UC-34). Khác với
    /// lỗi nghiệp vụ thông thường: kèm theo <see cref="ConflictId"/> để FE dẫn thẳng khách tới
    /// hàng đợi "Xung đột lịch giao" của Sales Manager thay vì chỉ báo lỗi chung chung.
    /// </summary>
    public class ScheduleConflictException : Exception
    {
        public Guid ConflictId { get; }

        public ScheduleConflictException(string message, Guid conflictId) : base(message)
        {
            ConflictId = conflictId;
        }
    }
}
