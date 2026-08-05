using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;

namespace VietTien.API.Services.Interfaces
{
    public interface IJobRunService
    {
        // Bọc 1 lần chạy job: tạo JobRun (Running) -> gọi job.RunAsync -> cập nhật Success/Failed.
        // KHÔNG throw ra ngoài - lỗi được ghi vào JobRun.ErrorMessage (đã redact), để 1 job lỗi
        // không làm sập vòng lặp nền hay API retry thủ công.
        Task<JobRunDto> RunTrackedAsync(IScheduledJob job, JobTriggerType triggerType, Guid? actorUserId, CancellationToken ct = default);

        Task<JobRun?> GetLastRunAsync(string jobName);
        Task<PagedResultDto<JobRunDto>> SearchAsync(JobRunQueryDto query);
        Task<List<JobHealthSummaryDto>> GetHealthSummaryAsync(IEnumerable<string> knownJobNames);
    }
}
