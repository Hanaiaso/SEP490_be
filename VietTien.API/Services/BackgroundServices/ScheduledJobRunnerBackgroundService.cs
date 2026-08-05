using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.BackgroundServices
{
    // Thay thế OrderSlaBackgroundService: 1 vòng lặp duy nhất, mỗi phút kiểm tra tất cả
    // IScheduledJob đã đăng ký, job nào đến hạn (dựa theo Interval riêng + lần chạy gần nhất
    // lưu trong bảng JobRun) thì chạy qua IJobRunService.RunTrackedAsync — cùng code path
    // với API "retry" thủ công trên System Health, nên lịch sử chạy luôn nhất quán.
    public class ScheduledJobRunnerBackgroundService : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ScheduledJobRunnerBackgroundService> _logger;

        public ScheduledJobRunnerBackgroundService(IServiceProvider serviceProvider, ILogger<ScheduledJobRunnerBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var jobRunService = scope.ServiceProvider.GetRequiredService<IJobRunService>();
                    var jobs = scope.ServiceProvider.GetServices<IScheduledJob>();

                    foreach (var job in jobs)
                    {
                        if (await IsDueAsync(jobRunService, job))
                        {
                            await jobRunService.RunTrackedAsync(job, JobTriggerType.Scheduled, actorUserId: null, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in ScheduledJobRunnerBackgroundService.");
                }

                await Task.Delay(TickInterval, stoppingToken);
            }
        }

        private static async Task<bool> IsDueAsync(IJobRunService jobRunService, IScheduledJob job)
        {
            var lastRun = await jobRunService.GetLastRunAsync(job.JobName);
            if (lastRun == null) return true;

            var elapsed = DateTime.UtcNow - lastRun.StartedAt;

            // Nếu lần chạy trước vẫn đang "Running" nhưng chưa quá 3x Interval, coi như đang chạy dở, bỏ qua tick này.
            // Quá 3x Interval mà vẫn "Running" thì coi như tiến trình cũ đã crash (mất kết nối) -> cho phép chạy lại.
            if (lastRun.Status == JobRunStatus.Running && elapsed < TimeSpan.FromTicks(job.Interval.Ticks * 3))
                return false;

            return elapsed >= job.Interval;
        }
    }
}
