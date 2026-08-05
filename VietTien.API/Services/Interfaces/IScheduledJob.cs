namespace VietTien.API.Services.Interfaces
{
    // Một scheduled job chạy nền, có thể được kích hoạt tự động (ScheduledJobRunnerBackgroundService)
    // hoặc thủ công (Admin bấm "retry" trên màn hình System Health) — cùng 1 code path duy nhất.
    public interface IScheduledJob
    {
        string JobName { get; }
        TimeSpan Interval { get; }

        // Trả về số lượng item đã xử lý. Ném exception nếu chạy thất bại — IJobRunService sẽ bắt và ghi lại.
        Task<int> RunAsync(CancellationToken ct);
    }
}
