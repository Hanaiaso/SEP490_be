using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Cart;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Product;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ CartController (116 dòng, trước đó 0%).</summary>
    public class CartControllerTests
    {
        private readonly Mock<ICartService> _service = new();
        private readonly CartController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public CartControllerTests() => _sut = new CartController(_service.Object).WithUser(_userId, "Customer");

        [Fact]
        public async Task GetCart_ReturnsOkAndPassesCallerId()
        {
            _service.Setup(s => s.GetCartAsync(_userId)).ReturnsAsync(new CartDto());

            (await _sut.GetCart()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetCartAsync(_userId), Times.Once, "giỏ phải lấy theo user trong JWT");
        }

        [Fact]
        public async Task GetCart_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            // GetUserId() ném UnauthorizedAccessException, controller chỉ có catch(Exception) -> 400.
            (await _sut.GetCart()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task AddItemToCart_WhenModelStateInvalid_Returns400WithoutCallingService()
        {
            _sut.WithInvalidModelState("ProductId", "bat buoc");

            (await _sut.AddItemToCart(new AddToCartRequestDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.AddItemToCartAsync(It.IsAny<Guid>(), It.IsAny<AddToCartRequestDto>()), Times.Never);
        }

        [Fact]
        public async Task AddItemToCart_WhenProfileIncomplete_Returns409WithCode()
        {
            _service.Setup(s => s.AddItemToCartAsync(It.IsAny<Guid>(), It.IsAny<AddToCartRequestDto>()))
                .ThrowsAsync(new ProfileIncompleteException("chua hoan thien ho so"));

            var result = await _sut.AddItemToCart(new AddToCartRequestDto());

            result.StatusOf().Should().Be(409);
            result.Result.Should().BeAssignableTo<ObjectResult>()
                .Which.Value!.ToString().Should().Contain("PROFILE_INCOMPLETE");
        }

        [Fact]
        public async Task AddItemToCart_Success_ReturnsOk()
        {
            _service.Setup(s => s.AddItemToCartAsync(_userId, It.IsAny<AddToCartRequestDto>()))
                .ReturnsAsync(new CartDto());

            (await _sut.AddItemToCart(new AddToCartRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AddItemToCart_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.AddItemToCartAsync(It.IsAny<Guid>(), It.IsAny<AddToCartRequestDto>()))
                .ThrowsAsync(new Exception("het hang"));

            (await _sut.AddItemToCart(new AddToCartRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateCartItemQuantity_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Quantity", "phai > 0");

            (await _sut.UpdateCartItemQuantity(Guid.NewGuid(), new UpdateCartItemRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateCartItemQuantity_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdateCartItemAsync(_userId, It.IsAny<Guid>(), It.IsAny<UpdateCartItemRequestDto>()))
                .ReturnsAsync(new CartDto());

            (await _sut.UpdateCartItemQuantity(Guid.NewGuid(), new UpdateCartItemRequestDto()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateCartItemQuantity_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UpdateCartItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateCartItemRequestDto>()))
                .ThrowsAsync(new Exception("khong thay item"));

            (await _sut.UpdateCartItemQuantity(Guid.NewGuid(), new UpdateCartItemRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RemoveItemFromCart_Success_ReturnsOk()
        {
            _service.Setup(s => s.RemoveItemFromCartAsync(_userId, It.IsAny<Guid>())).ReturnsAsync(new CartDto());

            (await _sut.RemoveItemFromCart(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task RemoveItemFromCart_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.RemoveItemFromCartAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.RemoveItemFromCart(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ClearCart_Success_ReturnsOk()
        {
            _service.Setup(s => s.ClearCartAsync(_userId)).ReturnsAsync(new CartDto());

            (await _sut.ClearCart()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ClearCart_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ClearCartAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.ClearCart()).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ ProductController (80 dòng, trước đó 0%).</summary>
    public class ProductControllerTests
    {
        private readonly Mock<IProductService> _service = new();
        private readonly ProductController _sut;

        public ProductControllerTests() => _sut = new ProductController(_service.Object).WithUser();

        [Fact]
        public async Task GetProducts_PassesAllFiltersThroughToService()
        {
            var categoryId = Guid.NewGuid();
            _service.Setup(s => s.GetProductsAsync(2, 24, categoryId, "mang boc", "price"))
                .ReturnsAsync(new ProductPagedResultDto());

            (await _sut.GetProducts(2, 24, categoryId, "mang boc", "price")).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetProductsAsync(2, 24, categoryId, "mang boc", "price"), Times.Once,
                "mọi tham số lọc/phân trang phải xuống service, không được nuốt mất");
        }

        [Fact]
        public async Task GetProductById_WhenNotFound_Returns404()
        {
            _service.Setup(s => s.GetProductByIdAsync(It.IsAny<Guid>())).ReturnsAsync((ProductDetailDto?)null);

            (await _sut.GetProductById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetProductById_WhenFound_ReturnsOk()
        {
            _service.Setup(s => s.GetProductByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new ProductDetailDto());

            (await _sut.GetProductById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetCategories_ReturnsOk()
        {
            _service.Setup(s => s.GetCategoriesAsync()).ReturnsAsync(new List<CategoryDto> { new() });

            (await _sut.GetCategories()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateProduct_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateProductAsync(It.IsAny<CreateProductDto>()))
                .ThrowsAsync(new Exception("trung SKU"));

            (await _sut.CreateProduct(new CreateProductDto())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>
    /// Case code-driven phủ NotificationsController (139 dòng, trước đó 0%).
    /// Controller này dùng thẳng ApplicationDbContext nên test bằng EF InMemory
    /// (`TestDbFactory`) chứ không mock được service.
    /// </summary>
    public class NotificationsControllerTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly NotificationsController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public NotificationsControllerTests()
            => _sut = new NotificationsController(_db).WithUser(_userId, "Customer");

        private async Task SeedAsync(int unread, int read)
        {
            for (var i = 0; i < unread; i++)
                _db.Notifications.Add(new Notification { RecipientUserId = _userId, Title = $"chua doc {i}", IsRead = false });
            for (var i = 0; i < read; i++)
                _db.Notifications.Add(new Notification { RecipientUserId = _userId, Title = $"da doc {i}", IsRead = true });
            // Thông báo của người khác — không được lẫn vào kết quả.
            _db.Notifications.Add(new Notification { RecipientUserId = Guid.NewGuid(), Title = "cua nguoi khac" });
            await _db.SaveChangesAsync();
        }

        [Fact]
        public async Task GetNotifications_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetNotifications()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetNotifications_ReturnsOnlyOwnNotifications()
        {
            await SeedAsync(unread: 2, read: 1);

            var result = await _sut.GetNotifications();

            result.StatusOf().Should().Be(200);
            result.Should().BeAssignableTo<ObjectResult>()
                .Which.Value!.ToString().Should().NotContain("cua nguoi khac",
                    "không được rò rỉ thông báo của user khác");
        }

        [Fact]
        public async Task GetUnreadCount_CountsOnlyUnreadOfCaller()
        {
            await SeedAsync(unread: 3, read: 2);

            var result = await _sut.GetUnreadCount();

            result.StatusOf().Should().Be(200);
            result.Should().BeAssignableTo<ObjectResult>()
                .Which.Value!.ToString().Should().Contain("3");
        }

        [Fact]
        public async Task GetUnreadCount_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetUnreadCount()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MarkAsRead_WhenNotFound_Returns404()
        {
            (await _sut.MarkAsRead(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task MarkAsRead_SetsIsReadTrue()
        {
            var notif = new Notification { RecipientUserId = _userId, Title = "x", IsRead = false };
            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();

            (await _sut.MarkAsRead(notif.Id)).StatusOf().Should().Be(200);
            (await _db.Notifications.FindAsync(notif.Id))!.IsRead.Should().BeTrue();
        }

        [Fact]
        public async Task MarkAllAsRead_MarksEveryUnreadOfCaller()
        {
            await SeedAsync(unread: 3, read: 0);

            (await _sut.MarkAllAsRead()).StatusOf().Should().Be(200);
            _db.Notifications.Count(n => n.RecipientUserId == _userId && !n.IsRead).Should().Be(0);
        }

        [Fact]
        public async Task DeleteNotification_WhenNotFound_Returns404()
        {
            (await _sut.DeleteNotification(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task DeleteNotification_RemovesRow()
        {
            var notif = new Notification { RecipientUserId = _userId, Title = "xoa" };
            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();

            (await _sut.DeleteNotification(notif.Id)).StatusOf().Should().Be(200);
            (await _db.Notifications.FindAsync(notif.Id)).Should().BeNull();
        }

        [Fact]
        public async Task DeleteAllRead_RemovesOnlyReadOnes()
        {
            await SeedAsync(unread: 2, read: 3);

            var result = await _sut.DeleteAllRead();

            result.StatusOf().Should().Be(200);
            _db.Notifications.Count(n => n.RecipientUserId == _userId && n.IsRead).Should().Be(0);
            _db.Notifications.Count(n => n.RecipientUserId == _userId && !n.IsRead)
                .Should().Be(2, "thông báo chưa đọc phải được giữ lại");
        }
    }

    /// <summary>
    /// Case code-driven phủ HandoverController (43 dòng, trước đó 0%).
    /// ⚠ Phát hiện khi viết test: cả 4 action đều trả GIÁ TRỊ CỨNG, không đọc/ghi DB,
    /// không có dependency nào. Đây là stub chưa cài đặt — xem mục defect trong manifest.
    /// </summary>
    public class HandoverControllerTests
    {
        private readonly HandoverController _sut = new HandoverController().WithUser();

        [Fact]
        public void GetHandoverById_ReturnsHardcodedPendingStatus()
        {
            var id = Guid.NewGuid();

            var result = _sut.GetHandoverById(id);

            result.StatusOf().Should().Be(200);
            result.Should().BeAssignableTo<ObjectResult>()
                .Which.Value!.ToString().Should().Contain("PENDING");
        }

        [Fact]
        public void CreateHandover_ReturnsOk()
        {
            _sut.CreateHandover().StatusOf().Should().Be(200);
        }

        [Fact]
        public void WarehouseConfirm_ReturnsOk()
        {
            _sut.WarehouseConfirm(Guid.NewGuid()).StatusOf().Should().Be(200);
        }

        [Fact]
        public void SalesConfirm_ReturnsOk()
        {
            _sut.SalesConfirm(Guid.NewGuid()).StatusOf().Should().Be(200);
        }
    }

    /// <summary>
    /// Case code-driven phủ SystemHealthController (97 dòng, trước đó 0%).
    /// Danh sách job được tiêm dưới dạng IEnumerable&lt;IScheduledJob&gt; nên test tự dựng
    /// job giả để chạm cả nhánh RetryJob tìm thấy / không tìm thấy và hàm DescribeInterval.
    /// </summary>
    public class SystemHealthControllerTests
    {
        private readonly Mock<IJobRunService> _jobRuns = new();
        private readonly Mock<IWebhookLogService> _webhooks = new();
        private readonly List<IScheduledJob> _jobs;
        private readonly SystemHealthController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        private sealed class StubJob : IScheduledJob
        {
            public StubJob(string name, TimeSpan interval) { JobName = name; Interval = interval; }
            public string JobName { get; }
            public TimeSpan Interval { get; }
            public Task<int> RunAsync(CancellationToken ct) => Task.FromResult(0);
        }

        public SystemHealthControllerTests()
        {
            _jobs = new List<IScheduledJob>
            {
                new StubJob("ReservationExpiryJob", TimeSpan.FromMinutes(15)),
                new StubJob("QuotationExpiryJob", TimeSpan.FromHours(6)),
                new StubJob("MonthlyReportJob", TimeSpan.FromDays(30))
            };
            _sut = new SystemHealthController(_jobRuns.Object, _webhooks.Object, _jobs).WithUser(_adminId, "Admin");
        }

        [Fact]
        public async Task SearchJobRuns_Success_ReturnsOk()
        {
            _jobRuns.Setup(s => s.SearchAsync(It.IsAny<JobRunQueryDto>()))
                .ReturnsAsync(new PagedResultDto<JobRunDto>());

            (await _sut.SearchJobRuns(new JobRunQueryDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SearchJobRuns_WhenServiceThrows_Returns400()
        {
            _jobRuns.Setup(s => s.SearchAsync(It.IsAny<JobRunQueryDto>()))
                .ThrowsAsync(new ArgumentException("khoang ngay khong hop le"));

            (await _sut.SearchJobRuns(new JobRunQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetJobRunsSummary_FillsIntervalDescriptionInAllThreeUnits()
        {
            _jobRuns.Setup(s => s.GetHealthSummaryAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<JobHealthSummaryDto>
                {
                    new() { JobName = "ReservationExpiryJob" },
                    new() { JobName = "QuotationExpiryJob" },
                    new() { JobName = "MonthlyReportJob" },
                    new() { JobName = "JobDaBiGoBo" }
                });

            var result = await _sut.GetJobRunsSummary();

            result.StatusOf().Should().Be(200);
            var rows = ((result as ObjectResult)!.Value as List<JobHealthSummaryDto>)!;
            rows.Single(r => r.JobName == "ReservationExpiryJob").IntervalDescription.Should().Be("15 phút");
            rows.Single(r => r.JobName == "QuotationExpiryJob").IntervalDescription.Should().Be("6 giờ");
            rows.Single(r => r.JobName == "MonthlyReportJob").IntervalDescription.Should().Be("30 ngày");
            rows.Single(r => r.JobName == "JobDaBiGoBo").IntervalDescription.Should().BeEmpty(
                "job có lịch sử chạy nhưng đã bị gỡ khỏi code thì để trống chứ không được nổ NullReference");
        }

        [Fact]
        public async Task GetJobRunsSummary_PassesDistinctSortedJobNames()
        {
            _jobRuns.Setup(s => s.GetHealthSummaryAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new List<JobHealthSummaryDto>());

            await _sut.GetJobRunsSummary();

            _jobRuns.Verify(s => s.GetHealthSummaryAsync(
                It.Is<IEnumerable<string>>(names => names.SequenceEqual(
                    new[] { "MonthlyReportJob", "QuotationExpiryJob", "ReservationExpiryJob" }))), Times.Once);
        }

        [Fact]
        public async Task RetryJob_WhenJobNameUnknown_Returns404()
        {
            (await _sut.RetryJob("KhongCoJobNay")).StatusOf().Should().Be(404);
            _jobRuns.Verify(s => s.RunTrackedAsync(It.IsAny<IScheduledJob>(), It.IsAny<JobTriggerType>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RetryJob_MatchesJobNameCaseInsensitivelyAndTagsManualTrigger()
        {
            _jobRuns.Setup(s => s.RunTrackedAsync(It.IsAny<IScheduledJob>(), It.IsAny<JobTriggerType>(),
                    It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JobRunDto());

            (await _sut.RetryJob("reservationexpiryjob")).StatusOf().Should().Be(200);

            _jobRuns.Verify(s => s.RunTrackedAsync(
                It.Is<IScheduledJob>(j => j.JobName == "ReservationExpiryJob"),
                JobTriggerType.Manual, _adminId, It.IsAny<CancellationToken>()), Times.Once,
                "chạy tay phải ghi nhận là Manual kèm id admin bấm nút");
        }

        [Fact]
        public async Task SearchWebhookLogs_Success_ReturnsOk()
        {
            _webhooks.Setup(s => s.SearchAsync(It.IsAny<WebhookLogQueryDto>()))
                .ReturnsAsync(new PagedResultDto<WebhookLogDto>());

            (await _sut.SearchWebhookLogs(new WebhookLogQueryDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SearchWebhookLogs_WhenServiceThrows_Returns400()
        {
            _webhooks.Setup(s => s.SearchAsync(It.IsAny<WebhookLogQueryDto>()))
                .ThrowsAsync(new ArgumentException("tham so sai"));

            (await _sut.SearchWebhookLogs(new WebhookLogQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RetryWebhookLog_Success_ReturnsOk()
        {
            _webhooks.Setup(s => s.RetryAsync(It.IsAny<Guid>())).ReturnsAsync(new WebhookLogDto());

            (await _sut.RetryWebhookLog(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task RetryWebhookLog_WhenLogMissing_Returns400()
        {
            _webhooks.Setup(s => s.RetryAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay webhook log"));

            (await _sut.RetryWebhookLog(Guid.NewGuid())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ WarehouseShiftController (37 dòng, trước đó 0%).</summary>
    public class WarehouseShiftControllerTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();

        [Fact]
        public async Task GetShifts_MapsTimeSpanToHhMmString()
        {
            _db.WarehouseShifts.Add(new WarehouseShift
            {
                Name = "Ca Sáng",
                StartTime = new TimeSpan(6, 0, 0),
                EndTime = new TimeSpan(14, 30, 0),
                Description = "buoi sang"
            });
            await _db.SaveChangesAsync();

            var sut = new WarehouseShiftController(_db).WithUser();
            var result = await sut.GetShifts();

            result.StatusOf().Should().Be(200);
            var dto = ((result.Result as ObjectResult)!.Value as List<WarehouseShiftDto>)!.Single();
            dto.StartTime.Should().Be("06:00");
            dto.EndTime.Should().Be("14:30", "TimeSpan phải được format sang hh:mm cho client");
        }

        [Fact]
        public async Task GetShifts_WhenEmpty_ReturnsEmptyList()
        {
            var sut = new WarehouseShiftController(_db).WithUser();

            var result = await sut.GetShifts();

            result.StatusOf().Should().Be(200);
            (result.Result as ObjectResult)!.Value.Should().BeAssignableTo<List<WarehouseShiftDto>>()
                .Which.Should().BeEmpty();
        }
    }
}
