using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/admin/system-health")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class SystemHealthController : ControllerBase
    {
        private readonly IJobRunService _jobRunService;
        private readonly IWebhookLogService _webhookLogService;
        private readonly IEnumerable<IScheduledJob> _jobs;

        public SystemHealthController(IJobRunService jobRunService, IWebhookLogService webhookLogService, IEnumerable<IScheduledJob> jobs)
        {
            _jobRunService = jobRunService;
            _webhookLogService = webhookLogService;
            _jobs = jobs;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        [HttpGet("job-runs")]
        public async Task<IActionResult> SearchJobRuns([FromQuery] JobRunQueryDto query)
        {
            try
            {
                var result = await _jobRunService.SearchAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("job-runs/summary")]
        public async Task<IActionResult> GetJobRunsSummary()
        {
            var jobNames = _jobs.Select(j => j.JobName).Distinct().OrderBy(n => n).ToList();
            var summary = await _jobRunService.GetHealthSummaryAsync(jobNames);

            foreach (var item in summary)
            {
                var job = _jobs.FirstOrDefault(j => j.JobName == item.JobName);
                item.IntervalDescription = job == null ? string.Empty : DescribeInterval(job.Interval);
            }

            return Ok(summary);
        }

        [HttpPost("job-runs/{jobName}/retry")]
        public async Task<IActionResult> RetryJob(string jobName)
        {
            var job = _jobs.FirstOrDefault(j => string.Equals(j.JobName, jobName, StringComparison.OrdinalIgnoreCase));
            if (job == null)
                return NotFound(new { message = $"Không tìm thấy job '{jobName}'." });

            var result = await _jobRunService.RunTrackedAsync(job, JobTriggerType.Manual, GetUserId());
            return Ok(result);
        }

        [HttpGet("webhook-logs")]
        public async Task<IActionResult> SearchWebhookLogs([FromQuery] WebhookLogQueryDto query)
        {
            try
            {
                var result = await _webhookLogService.SearchAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("webhook-logs/{id}/retry")]
        public async Task<IActionResult> RetryWebhookLog(Guid id)
        {
            try
            {
                var result = await _webhookLogService.RetryAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static string DescribeInterval(TimeSpan interval)
        {
            if (interval.TotalMinutes < 60) return $"{(int)interval.TotalMinutes} phút";
            if (interval.TotalHours < 24) return $"{(int)interval.TotalHours} giờ";
            return $"{(int)interval.TotalDays} ngày";
        }
    }
}
