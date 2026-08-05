using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class JobRunService : IJobRunService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<JobRunService> _logger;

        public JobRunService(ApplicationDbContext context, ILogger<JobRunService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<JobRunDto> RunTrackedAsync(IScheduledJob job, JobTriggerType triggerType, Guid? actorUserId, CancellationToken ct = default)
        {
            var run = new JobRun
            {
                JobName = job.JobName,
                Status = JobRunStatus.Running,
                StartedAt = DateTime.UtcNow,
                TriggerType = triggerType,
                TriggeredByUserId = actorUserId
            };

            _context.JobRuns.Add(run);
            await _context.SaveChangesAsync(ct);

            try
            {
                var itemsProcessed = await job.RunAsync(ct);

                run.Status = JobRunStatus.Success;
                run.ItemsProcessed = itemsProcessed;
                run.FinishedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled job {JobName} failed.", job.JobName);

                run.Status = JobRunStatus.Failed;
                run.ErrorMessage = SensitiveDataRedactor.RedactPlainText(ex.Message, ex.GetType().Name);
                run.FinishedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
            return ToDto(run);
        }

        public async Task<JobRun?> GetLastRunAsync(string jobName)
        {
            return await _context.JobRuns
                .AsNoTracking()
                .Where(r => r.JobName == jobName)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResultDto<JobRunDto>> SearchAsync(JobRunQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

            var source = _context.JobRuns.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.JobName))
                source = source.Where(r => r.JobName == query.JobName);

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                if (!Enum.TryParse<JobRunStatus>(query.Status, true, out var status))
                    throw new Exception($"Status '{query.Status}' không hợp lệ.");
                source = source.Where(r => r.Status == status);
            }

            if (query.FromDate.HasValue)
                source = source.Where(r => r.StartedAt >= query.FromDate.Value);

            if (query.ToDate.HasValue)
                source = source.Where(r => r.StartedAt <= query.ToDate.Value);

            var totalCount = await source.CountAsync();

            var entities = await source
                .OrderByDescending(r => r.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResultDto<JobRunDto>
            {
                Items = entities.Select(ToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }

        public async Task<List<JobHealthSummaryDto>> GetHealthSummaryAsync(IEnumerable<string> knownJobNames)
        {
            var todayStart = DateTime.UtcNow.Date;
            var result = new List<JobHealthSummaryDto>();

            foreach (var jobName in knownJobNames)
            {
                var lastRun = await _context.JobRuns
                    .AsNoTracking()
                    .Where(r => r.JobName == jobName)
                    .OrderByDescending(r => r.StartedAt)
                    .FirstOrDefaultAsync();

                var successToday = await _context.JobRuns.AsNoTracking()
                    .CountAsync(r => r.JobName == jobName && r.Status == JobRunStatus.Success && r.StartedAt >= todayStart);

                var failureToday = await _context.JobRuns.AsNoTracking()
                    .CountAsync(r => r.JobName == jobName && r.Status == JobRunStatus.Failed && r.StartedAt >= todayStart);

                result.Add(new JobHealthSummaryDto
                {
                    JobName = jobName,
                    LastRun = lastRun == null ? null : ToDto(lastRun),
                    TodaySuccessCount = successToday,
                    TodayFailureCount = failureToday,
                    IsCurrentlyRunning = lastRun != null && lastRun.Status == JobRunStatus.Running
                });
            }

            return result;
        }

        private static JobRunDto ToDto(JobRun r) => new()
        {
            Id = r.Id,
            JobName = r.JobName,
            Status = r.Status.ToString(),
            StartedAt = r.StartedAt,
            FinishedAt = r.FinishedAt,
            DurationMs = r.FinishedAt.HasValue ? (long)(r.FinishedAt.Value - r.StartedAt).TotalMilliseconds : null,
            ItemsProcessed = r.ItemsProcessed,
            ErrorMessage = r.ErrorMessage,
            TriggerType = r.TriggerType.ToString(),
            TriggeredByUserId = r.TriggeredByUserId
        };
    }
}
