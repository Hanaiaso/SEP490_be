using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Exceptions;
using VietTien.API.Hubs;
using VietTien.API.Infrastructure.Middleware;
using VietTien.API.Models;
using VietTien.API.Services.BackgroundServices;
using VietTien.API.Services.Interfaces;
using Xunit;

namespace VietTien.Tests.Infrastructure
{
    /// <summary>
    /// Case code-driven phủ ExceptionHandlingMiddleware (trước đó 20,4%).
    /// Middleware là lưới an toàn cuối cùng: nó chỉ chạy khi controller KHÔNG tự bắt exception,
    /// nên gần như không chạm được qua test HTTP thật — phải gọi trực tiếp với RequestDelegate giả.
    /// </summary>
    public class ExceptionHandlingMiddlewareTests
    {
        private static async Task<(int Status, string Body)> RunAsync(Exception thrown)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/orders/place-order";
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var middleware = new ExceptionHandlingMiddleware(
                _ => throw thrown,
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            responseBody.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(responseBody).ReadToEndAsync());
        }

        /// <summary>
        /// Đọc trường `message` đã giải mã. Cần thiết vì `JsonSerializer` mặc định escape
        /// mọi ký tự ngoài ASCII, nên tiếng Việt trong body thô là chuỗi \uXXXX.
        /// </summary>
        private static string MessageOf(string body)
            => JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;

        [Fact]
        public async Task WhenNoException_PassesThroughUntouched()
        {
            var context = new DefaultHttpContext();
            var nextWasCalled = false;
            var middleware = new ExceptionHandlingMiddleware(
                _ => { nextWasCalled = true; return Task.CompletedTask; },
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            nextWasCalled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(200, "không có lỗi thì middleware không được đụng vào response");
        }

        [Fact]
        public async Task ProfileIncompleteException_Maps409WithCode()
        {
            var (status, body) = await RunAsync(new ProfileIncompleteException("chua hoan thien ho so"));

            status.Should().Be(409);
            body.Should().Contain("PROFILE_INCOMPLETE",
                "giữ đúng hành vi CartController đang trả cho FE");
        }

        [Fact]
        public async Task KeyNotFoundException_Maps404()
        {
            var (status, _) = await RunAsync(new KeyNotFoundException("khong thay don"));

            status.Should().Be(404);
        }

        [Fact]
        public async Task UnauthorizedAccessException_Maps403()
        {
            var (status, _) = await RunAsync(new UnauthorizedAccessException("khong co quyen"));

            status.Should().Be(403);
        }

        [Fact]
        public async Task InvalidOperationException_Maps409()
        {
            var (status, _) = await RunAsync(new InvalidOperationException("sai trang thai"));

            status.Should().Be(409);
        }

        [Fact]
        public async Task DbUpdateConcurrencyException_Maps409WithGenericMessage()
        {
            var (status, body) = await RunAsync(new DbUpdateConcurrencyException());

            status.Should().Be(409);
            MessageOf(body).Should().Contain("tải lại", "thông điệp phải hướng dẫn người dùng tải lại");
        }

        [Fact]
        public async Task ArgumentException_Maps400()
        {
            var (status, _) = await RunAsync(new ArgumentException("tham so sai"));

            status.Should().Be(400);
        }

        [Fact]
        public async Task FormatException_Maps400()
        {
            var (status, _) = await RunAsync(new FormatException("sai dinh dang"));

            status.Should().Be(400);
        }

        [Fact]
        public async Task PlainException_Maps400BecauseCodebaseUsesItForBusinessErrors()
        {
            // Quy ước có sẵn trong code: `throw new Exception("Không đủ hàng...")` = lỗi nghiệp vụ.
            var (status, body) = await RunAsync(new Exception("Không đủ hàng trong kho"));

            status.Should().Be(400);
            MessageOf(body).Should().Contain("Không đủ hàng", "lỗi nghiệp vụ phải giữ nguyên thông điệp cho người dùng");
        }

        [Fact]
        public async Task UnexpectedSubclass_Maps500AndHidesDetail()
        {
            var (status, body) = await RunAsync(new NullReferenceException("Object reference not set to ThuKhoService.x"));

            status.Should().Be(500);
            var message = MessageOf(body);
            message.Should().NotContain("ThuKhoService",
                "lỗi hệ thống ngoài dự kiến không được lộ chi tiết nội bộ ra ngoài");
            message.Should().Contain("vui lòng thử lại sau");
        }

        [Fact]
        public async Task ResponseIsAlwaysValidJson()
        {
            var (_, body) = await RunAsync(new KeyNotFoundException("x"));

            var act = () => JsonDocument.Parse(body);
            act.Should().NotThrow("FE luôn parse JSON nên body lỗi cũng phải là JSON hợp lệ");
        }

        [Fact]
        public async Task WhenResponseAlreadyStarted_RethrowsInsteadOfCorruptingIt()
        {
            var context = new DefaultHttpContext();
            var responseFeature = new Mock<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>();
            responseFeature.SetupGet(f => f.HasStarted).Returns(true);
            responseFeature.SetupGet(f => f.Headers).Returns(new HeaderDictionary());
            responseFeature.SetupGet(f => f.Body).Returns(new MemoryStream());
            context.Features.Set(responseFeature.Object);

            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new KeyNotFoundException("x"),
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            var act = async () => await middleware.InvokeAsync(context);

            // Response đã bắt đầu ghi -> không thể đổi status code nữa, middleware phải ném lại.
            await act.Should().ThrowAsync<KeyNotFoundException>();
        }
    }

    /// <summary>
    /// Case code-driven phủ ScheduledJobRunnerBackgroundService (trước đó 0%).
    /// Vòng lặp tick mỗi phút nên test phải điều khiển bằng tín hiệu (TaskCompletionSource)
    /// thay vì chờ thời gian thật.
    /// </summary>
    public class ScheduledJobRunnerBackgroundServiceTests
    {
        private sealed class StubJob : IScheduledJob
        {
            public StubJob(string name, TimeSpan interval) { JobName = name; Interval = interval; }
            public string JobName { get; }
            public TimeSpan Interval { get; }
            public Task<int> RunAsync(CancellationToken ct) => Task.FromResult(0);
        }

        private readonly Mock<IJobRunService> _jobRuns = new();

        /// <summary>
        /// Chạy service đúng một tick rồi dừng. Job "canary" luôn đến hạn và đứng CUỐI danh sách:
        /// khi nó chạy thì quyết định cho các job trước đó đã xong -> assert tất định, không sleep.
        /// </summary>
        private async Task RunOneTickAsync(params IScheduledJob[] jobsUnderTest)
        {
            var canary = new StubJob("CanaryJob", TimeSpan.FromMinutes(1));
            var tickDone = new TaskCompletionSource();

            _jobRuns.Setup(s => s.GetLastRunAsync("CanaryJob")).ReturnsAsync((JobRun?)null);
            _jobRuns.Setup(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "CanaryJob"),
                    It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Callback(() => tickDone.TrySetResult())
                .ReturnsAsync(new JobRunDto());

            var services = new ServiceCollection();
            services.AddSingleton(_jobRuns.Object);
            foreach (var job in jobsUnderTest) services.AddSingleton<IScheduledJob>(job);
            services.AddSingleton<IScheduledJob>(canary);
            await using var provider = services.BuildServiceProvider();

            var sut = new ScheduledJobRunnerBackgroundService(
                provider, NullLogger<ScheduledJobRunnerBackgroundService>.Instance);

            await sut.StartAsync(CancellationToken.None);
            await tickDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await sut.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task WhenJobNeverRan_RunsItTaggedAsScheduledWithNoActor()
        {
            var job = new StubJob("NewJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("NewJob")).ReturnsAsync((JobRun?)null);
            _jobRuns.Setup(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "NewJob"),
                    It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobRunDto());

            await RunOneTickAsync(job);

            _jobRuns.Verify(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "NewJob"),
                JobTriggerType.Scheduled, null, It.IsAny<CancellationToken>()), Times.Once,
                "chạy tự động phải ghi nhận là Scheduled và không có actor");
        }

        [Fact]
        public async Task WhenIntervalNotElapsed_SkipsJob()
        {
            var job = new StubJob("RecentJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("RecentJob")).ReturnsAsync(new JobRun
            {
                JobName = "RecentJob",
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                Status = JobRunStatus.Success
            });

            await RunOneTickAsync(job);

            _jobRuns.Verify(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "RecentJob"),
                It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task WhenIntervalElapsed_RunsJob()
        {
            var job = new StubJob("DueJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("DueJob")).ReturnsAsync(new JobRun
            {
                JobName = "DueJob",
                StartedAt = DateTime.UtcNow.AddMinutes(-20),
                Status = JobRunStatus.Success
            });
            _jobRuns.Setup(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "DueJob"),
                    It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobRunDto());

            await RunOneTickAsync(job);

            _jobRuns.Verify(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "DueJob"),
                It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WhenPreviousRunStillRunningWithinGracePeriod_SkipsToAvoidDoubleRun()
        {
            var job = new StubJob("LongJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("LongJob")).ReturnsAsync(new JobRun
            {
                JobName = "LongJob",
                StartedAt = DateTime.UtcNow.AddMinutes(-20),   // đã quá Interval…
                Status = JobRunStatus.Running                   // …nhưng chưa quá 3x (45 phút)
            });

            await RunOneTickAsync(job);

            _jobRuns.Verify(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "LongJob"),
                It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never,
                "job đang chạy dở không được kích hoạt lần hai");
        }

        [Fact]
        public async Task WhenPreviousRunStuckBeyondGracePeriod_RunsAgain()
        {
            var job = new StubJob("StuckJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("StuckJob")).ReturnsAsync(new JobRun
            {
                JobName = "StuckJob",
                StartedAt = DateTime.UtcNow.AddMinutes(-60),   // quá 3x Interval (45 phút)
                Status = JobRunStatus.Running
            });
            _jobRuns.Setup(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "StuckJob"),
                    It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobRunDto());

            await RunOneTickAsync(job);

            _jobRuns.Verify(s => s.RunTrackedAsync(It.Is<IScheduledJob>(j => j.JobName == "StuckJob"),
                It.IsAny<JobTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once,
                "tiến trình cũ coi như đã crash -> phải cho chạy lại, không treo vĩnh viễn");
        }

        [Fact]
        public async Task WhenJobRunServiceThrows_LoopSurvivesInsteadOfCrashing()
        {
            var job = new StubJob("BrokenJob", TimeSpan.FromMinutes(15));
            _jobRuns.Setup(s => s.GetLastRunAsync("BrokenJob")).ThrowsAsync(new Exception("mat ket noi DB"));

            var tickAttempted = new TaskCompletionSource();
            _jobRuns.Setup(s => s.GetLastRunAsync("BrokenJob"))
                .Callback(() => tickAttempted.TrySetResult())
                .ThrowsAsync(new Exception("mat ket noi DB"));

            var services = new ServiceCollection();
            services.AddSingleton(_jobRuns.Object);
            services.AddSingleton<IScheduledJob>(job);
            await using var provider = services.BuildServiceProvider();

            var sut = new ScheduledJobRunnerBackgroundService(
                provider, NullLogger<ScheduledJobRunnerBackgroundService>.Instance);

            await sut.StartAsync(CancellationToken.None);
            await tickAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var act = async () => await sut.StopAsync(CancellationToken.None);
            await act.Should().NotThrowAsync("một job lỗi không được làm sập vòng lặp nền");
        }
    }

    /// <summary>Case code-driven phủ NotificationHub / SalesHub / WarehouseHub / ChatHub (cả 4 trước đó 0%).</summary>
    public class HubTests
    {
        private const string ConnectionId = "conn-1";

        private static Mock<IGroupManager> AttachContext(Hub hub, Guid? userId = null, string? role = null)
        {
            var claims = new List<Claim>();
            if (userId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
            if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));

            var context = new Mock<HubCallerContext>();
            context.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
            context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")));
            hub.Context = context.Object;

            var groups = new Mock<IGroupManager>();
            hub.Groups = groups.Object;
            return groups;
        }

        // ── NotificationHub ──────────────────────────────────────────────────

        [Fact]
        public async Task NotificationHub_OnConnected_JoinsBothUserAndRoleGroups()
        {
            var hub = new NotificationHub();
            var userId = Guid.NewGuid();
            var groups = AttachContext(hub, userId, "SalesManager");

            await hub.OnConnectedAsync();

            groups.Verify(g => g.AddToGroupAsync(ConnectionId, $"User_{userId}", It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(g => g.AddToGroupAsync(ConnectionId, "Role_SalesManager", It.IsAny<CancellationToken>()), Times.Once,
                "nhóm theo role để gửi thông báo hàng loạt");
        }

        [Fact]
        public async Task NotificationHub_OnConnected_WithoutClaims_JoinsNothing()
        {
            var hub = new NotificationHub();
            var groups = AttachContext(hub);

            await hub.OnConnectedAsync();

            groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never, "không có claim thì không được gán vào nhóm nào");
        }

        [Fact]
        public async Task NotificationHub_OnDisconnected_LeavesBothGroups()
        {
            var hub = new NotificationHub();
            var userId = Guid.NewGuid();
            var groups = AttachContext(hub, userId, "Customer");

            await hub.OnDisconnectedAsync(null);

            groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, $"User_{userId}", It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "Role_Customer", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NotificationHub_OnDisconnected_WithoutClaims_LeavesNothing()
        {
            var hub = new NotificationHub();
            var groups = AttachContext(hub);

            await hub.OnDisconnectedAsync(new Exception("mat ket noi"));

            groups.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── SalesHub / WarehouseHub ──────────────────────────────────────────

        [Fact]
        public async Task SalesHub_JoinsAndLeavesSalesStaffGroup()
        {
            var hub = new SalesHub();
            var groups = AttachContext(hub, Guid.NewGuid(), "SalesStaff");

            await hub.OnConnectedAsync();
            await hub.OnDisconnectedAsync(null);

            groups.Verify(g => g.AddToGroupAsync(ConnectionId, "SalesStaff", It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "SalesStaff", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WarehouseHub_JoinsAndLeavesWarehouseStaffGroup()
        {
            var hub = new WarehouseHub();
            var groups = AttachContext(hub, Guid.NewGuid(), "WarehouseStaff");

            await hub.OnConnectedAsync();
            await hub.OnDisconnectedAsync(null);

            groups.Verify(g => g.AddToGroupAsync(ConnectionId, "WarehouseStaff", It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "WarehouseStaff", It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── ChatHub ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ChatHub_JoinQuotationChat_ChecksPermissionBeforeJoiningGroup()
        {
            var service = new Mock<IQuotationService>();
            var hub = new ChatHub(service.Object);
            var userId = Guid.NewGuid();
            var groups = AttachContext(hub, userId, "Customer");
            var quotationId = Guid.NewGuid();
            service.Setup(s => s.GetMessagesAsync(quotationId, userId, "Customer"))
                .ReturnsAsync(new List<ChatMessageDto>());

            await hub.JoinQuotationChat(quotationId.ToString());

            service.Verify(s => s.GetMessagesAsync(quotationId, userId, "Customer"), Times.Once);
            groups.Verify(g => g.AddToGroupAsync(ConnectionId, quotationId.ToString(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ChatHub_JoinQuotationChat_WhenNotParticipant_DoesNotJoinGroup()
        {
            var service = new Mock<IQuotationService>();
            var hub = new ChatHub(service.Object);
            var groups = AttachContext(hub, Guid.NewGuid(), "Customer");
            service.Setup(s => s.GetMessagesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            var act = async () => await hub.JoinQuotationChat(Guid.NewGuid().ToString());

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never, "chặn nghe lén nhóm chat của báo giá người khác");
        }

        [Fact]
        public async Task ChatHub_LeaveQuotationChat_RemovesFromGroup()
        {
            var hub = new ChatHub(Mock.Of<IQuotationService>());
            var groups = AttachContext(hub, Guid.NewGuid(), "Customer");
            var quotationId = Guid.NewGuid().ToString();

            await hub.LeaveQuotationChat(quotationId);

            groups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, quotationId, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ChatHub_SendMessage_PersistsThenBroadcastsToGroup()
        {
            var service = new Mock<IQuotationService>();
            var hub = new ChatHub(service.Object);
            var userId = Guid.NewGuid();
            AttachContext(hub, userId, "Customer");

            var quotationId = Guid.NewGuid();
            var saved = new ChatMessageDto { MessageText = "chao anh" };
            service.Setup(s => s.SendMessageAsync(quotationId, userId,
                    It.Is<SendChatMessageRequest>(r => r.MessageText == "chao anh")))
                .ReturnsAsync(saved);

            var proxy = new Mock<IClientProxy>();
            var clients = new Mock<IHubCallerClients>();
            clients.Setup(c => c.Group(quotationId.ToString())).Returns(proxy.Object);
            hub.Clients = clients.Object;

            await hub.SendMessage(quotationId.ToString(), "chao anh");

            service.Verify(s => s.SendMessageAsync(quotationId, userId, It.IsAny<SendChatMessageRequest>()), Times.Once,
                "tin nhắn phải được lưu trước khi phát đi");
            proxy.Verify(p => p.SendCoreAsync("ReceiveMessage",
                It.Is<object?[]>(args => ReferenceEquals(args[0], saved)), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
