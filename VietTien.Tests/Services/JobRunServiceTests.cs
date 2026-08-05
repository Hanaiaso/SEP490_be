using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: JobRunService — L1-JOB-01, 02, 04, 05, 06, 07. EF InMemory + mock ILogger.
    /// ⚠ L1-JOB-03 (BVA số lần thử lại 0/max-1/max/max+1) BỊ BLOCKED ở tầng L1: JobRunService
    ///    KHÔNG có cơ chế retry — nó chỉ bọc đúng 1 lần chạy. Retry là chuyện riêng của từng job
    ///    (vd WebhookLogService.MaxAttempts, đã phủ bởi L1-WHL-07). Xem DOC_MISMATCHES.md.
    /// ⚠ Signature thật: RunTrackedAsync(IScheduledJob job, JobTriggerType, Guid? actorUserId, ct)
    ///    — nhận cả object job chứ không phải (tên, delegate) như doc mô tả;
    ///    GetHealthSummaryAsync(IEnumerable&lt;string&gt; knownJobNames) cần danh sách job đã biết.
    /// </summary>
    public class JobRunServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<ILogger<JobRunService>> _logger = new();
        private readonly JobRunService _sut;

        public JobRunServiceTests()
        {
            _sut = new JobRunService(_db, _logger.Object);
        }

        /// <summary>Job giả: hoặc trả về số item đã xử lý, hoặc ném lỗi.</summary>
        private sealed class StubJob : IScheduledJob
        {
            private readonly Func<Task<int>> _run;
            public string JobName { get; }
            public TimeSpan Interval => TimeSpan.FromMinutes(1);

            public StubJob(string name, Func<Task<int>> run) { JobName = name; _run = run; }

            public static StubJob Succeeding(string name, int items = 3) => new(name, () => Task.FromResult(items));
            public static StubJob Failing(string name, string message = "Job lỗi") => new(name, () => throw new Exception(message));

            public Task<int> RunAsync(CancellationToken ct) => _run();
        }

        // ── Block: RunTrackedAsync() ────────────────────────────────────────

        // L1-JOB-01 | EP-Valid | Job chạy thành công -> ghi start, finish, result = Success
        [Fact]
        public async Task L1_JOB_01_RunTracked_Success_RecordsStartFinishAndResult()
        {
            var dto = await _sut.RunTrackedAsync(StubJob.Succeeding("JobA", items: 7), JobTriggerType.Scheduled, actorUserId: null);

            dto.Status.Should().Be(nameof(JobRunStatus.Success));
            dto.ItemsProcessed.Should().Be(7);
            dto.ErrorMessage.Should().BeNull();

            var run = _db.JobRuns.Single();
            run.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            run.FinishedAt.Should().NotBeNull();
        }

        // L1-JOB-02 | EP-Invalid | Job ném lỗi -> bản ghi Failed kèm nội dung lỗi, KHÔNG có bản ghi Success
        [Fact]
        public async Task L1_JOB_02_RunTracked_Failure_NeverMarkedSuccess()
        {
            var dto = await _sut.RunTrackedAsync(StubJob.Failing("JobA", "Kết nối SePay timeout"), JobTriggerType.Scheduled, null);

            dto.Status.Should().Be(nameof(JobRunStatus.Failed));
            dto.ErrorMessage.Should().Contain("Kết nối SePay timeout");
            _db.JobRuns.Should().ContainSingle().Which.Status.Should().Be(JobRunStatus.Failed);
            _db.JobRuns.Any(r => r.Status == JobRunStatus.Success).Should().BeFalse();
        }

        // L1-JOB-04 | EP-Invalid | Lỗi của job KHÔNG được nuốt im lặng: phải vừa ghi log vừa lưu bản ghi Failed
        [Fact]
        public async Task L1_JOB_04_RunTracked_Failure_IsLoggedNotSwallowedSilently()
        {
            await _sut.RunTrackedAsync(StubJob.Failing("JobA"), JobTriggerType.Scheduled, null);

            _logger.Verify(l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once, "lỗi job phải để lại vết trong log vận hành");

            _db.JobRuns.Single().ErrorMessage.Should().NotBeNullOrWhiteSpace("và cả trong lịch sử chạy job");
        }

        // ── Block: GetLastRunAsync() / SearchAsync() / GetHealthSummaryAsync() ──

        // L1-JOB-05 | EP-Valid | GetLastRun trả đúng lần chạy gần nhất theo thời điểm bắt đầu
        [Fact]
        public async Task L1_JOB_05_GetLastRun_ReturnsMostRecent()
        {
            _db.JobRuns.AddRange(
                TestData.JobRun("JobA", r => r.StartedAt = DateTime.UtcNow.AddHours(-3)),
                TestData.JobRun("JobA", r => { r.StartedAt = DateTime.UtcNow.AddHours(-1); r.ItemsProcessed = 42; }),
                TestData.JobRun("JobA", r => r.StartedAt = DateTime.UtcNow.AddHours(-2)));
            _db.SaveChanges();

            var last = await _sut.GetLastRunAsync("JobA");

            last.Should().NotBeNull();
            last!.ItemsProcessed.Should().Be(42);
        }

        // L1-JOB-06 | EP-Valid | Health summary phản ánh đúng job đang lỗi và job khoẻ
        [Fact]
        public async Task L1_JOB_06_HealthSummary_ReflectsFailingJob()
        {
            _db.JobRuns.AddRange(
                TestData.JobRun("JobA", r => r.StartedAt = DateTime.UtcNow.AddMinutes(-10)),
                TestData.JobRun("JobB", r => { r.StartedAt = DateTime.UtcNow.AddMinutes(-5); r.Status = JobRunStatus.Failed; r.ErrorMessage = "lỗi"; }));
            _db.SaveChanges();

            var summary = await _sut.GetHealthSummaryAsync(new[] { "JobA", "JobB" });

            summary.Single(s => s.JobName == "JobA").LastRun!.Status.Should().Be(nameof(JobRunStatus.Success));
            summary.Single(s => s.JobName == "JobA").TodayFailureCount.Should().Be(0);
            summary.Single(s => s.JobName == "JobB").LastRun!.Status.Should().Be(nameof(JobRunStatus.Failed));
            summary.Single(s => s.JobName == "JobB").TodayFailureCount.Should().Be(1);
        }

        // L1-JOB-07 | EP-Valid | Job chưa từng chạy -> LastRun = null, KHÔNG bị coi là đang khoẻ mạnh
        [Fact]
        public async Task L1_JOB_07_HealthSummary_NeverRunJob_IsNotReportedHealthy()
        {
            _db.JobRuns.Add(TestData.JobRun("JobA"));
            _db.SaveChanges();

            var summary = await _sut.GetHealthSummaryAsync(new[] { "JobA", "JobC" });

            var jobC = summary.Single(s => s.JobName == "JobC");
            jobC.LastRun.Should().BeNull("chưa chạy lần nào — không được im lặng coi như khoẻ");
            jobC.TodaySuccessCount.Should().Be(0);
            jobC.IsCurrentlyRunning.Should().BeFalse();
        }

        // Bổ trợ cho JOB-05/06: SearchAsync lọc theo tên job + trạng thái
        [Fact]
        public async Task L1_JOB_05_Search_FiltersByJobNameAndStatus()
        {
            _db.JobRuns.AddRange(
                TestData.JobRun("JobA"),
                TestData.JobRun("JobA", r => r.Status = JobRunStatus.Failed),
                TestData.JobRun("JobB", r => r.Status = JobRunStatus.Failed));
            _db.SaveChanges();

            var result = await _sut.SearchAsync(new JobRunQueryDto { JobName = "JobA", Status = "Failed" });

            result.TotalCount.Should().Be(1);
            result.Items.Should().OnlyContain(i => i.JobName == "JobA" && i.Status == "Failed");
        }
    }
}
