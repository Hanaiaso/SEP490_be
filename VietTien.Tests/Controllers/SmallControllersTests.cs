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
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly Guid _actorId = Guid.NewGuid();
        private readonly ProductController _sut;

        public ProductControllerTests() => _sut = new ProductController(_service.Object, _audit.Object).WithUser(_actorId, "Admin");

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

        // ── UpdateProduct / DeleteProduct: RowVersion concurrency (2 CEO/Admin cùng sửa 1 sản phẩm) ──

        [Fact]
        public async Task UpdateProduct_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.UpdateProductAsync(It.IsAny<Guid>(), It.IsAny<UpdateProductDto>()))
                .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());

            (await _sut.UpdateProduct(Guid.NewGuid(), new UpdateProductDto())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdateProduct_WhenNotFound_Returns404()
        {
            _service.Setup(s => s.UpdateProductAsync(It.IsAny<Guid>(), It.IsAny<UpdateProductDto>()))
                .ThrowsAsync(new KeyNotFoundException("Không tìm thấy sản phẩm."));

            (await _sut.UpdateProduct(Guid.NewGuid(), new UpdateProductDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateProduct_Success_ReturnsOk()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.UpdateProductAsync(id, It.IsAny<UpdateProductDto>()))
                .ReturnsAsync(new ProductDetailDto());

            (await _sut.UpdateProduct(id, new UpdateProductDto())).StatusOf().Should().Be(200);
            _audit.Verify(a => a.LogAsync(
                "Product", id.ToString(), "UPDATE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteProduct_WhenConcurrent_Returns409()
        {
            _service.Setup(s => s.DeleteProductAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());

            (await _sut.DeleteProduct(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task DeleteProduct_WhenNotFound_Returns404()
        {
            _service.Setup(s => s.DeleteProductAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("Không tìm thấy sản phẩm."));

            (await _sut.DeleteProduct(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task DeleteProduct_Success_ReturnsNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.DeleteProductAsync(id)).Returns(Task.CompletedTask);

            (await _sut.DeleteProduct(id)).StatusOf().Should().Be(204);
            _audit.Verify(a => a.LogAsync(
                "Product", id.ToString(), "DELETE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), null, null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task CreateProduct_Success_WritesAuditLog()
        {
            var newId = Guid.NewGuid();
            _service.Setup(s => s.CreateProductAsync(It.IsAny<CreateProductDto>()))
                .ReturnsAsync(new ProductDetailDto { Id = newId });

            await _sut.CreateProduct(new CreateProductDto());

            _audit.Verify(a => a.LogAsync(
                "Product", newId.ToString(), "CREATE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                null, It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task CreateCategory_Success_WritesAuditLog()
        {
            var newId = Guid.NewGuid();
            _service.Setup(s => s.CreateCategoryAsync(It.IsAny<CreateCategoryRequest>()))
                .ReturnsAsync(new CategoryDto { Id = newId, Name = "Danh muc moi" });

            await _sut.CreateCategory(new CreateCategoryRequest());

            _audit.Verify(a => a.LogAsync(
                "ProductCategory", newId.ToString(), "CREATE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                null, It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task UpdateCategory_Success_WritesAuditLog()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetCategoriesForManagementAsync())
                .ReturnsAsync(new List<CategoryDto> { new() { Id = id, Name = "Cu" } });
            _service.Setup(s => s.UpdateCategoryAsync(id, It.IsAny<UpdateCategoryRequest>()))
                .ReturnsAsync(new CategoryDto { Id = id, Name = "Moi" });

            await _sut.UpdateCategory(id, new UpdateCategoryRequest());

            _audit.Verify(a => a.LogAsync(
                "ProductCategory", id.ToString(), "UPDATE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteCategory_Success_WritesAuditLog()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetCategoriesForManagementAsync())
                .ReturnsAsync(new List<CategoryDto> { new() { Id = id, Name = "Sap xoa" } });
            _service.Setup(s => s.DeleteCategoryAsync(id)).Returns(Task.CompletedTask);

            await _sut.DeleteCategory(id);

            _audit.Verify(a => a.LogAsync(
                "ProductCategory", id.ToString(), "DELETE",
                _actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), null, null, It.IsAny<string>()), Times.Once);
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
    /// Case code-driven phủ SystemHealthController (97 dòng, trước đó 0%).
    /// Danh sách job được tiêm dưới dạng IEnumerable&lt;IScheduledJob&gt; nên test tự dựng
    /// job giả để chạm cả nhánh RetryJob tìm thấy / không tìm thấy và hàm DescribeInterval.
    /// </summary>
    public class SystemHealthControllerTests
    {
        private readonly Mock<IJobRunService> _jobRuns = new();
        private readonly Mock<IWebhookLogService> _webhooks = new();
        private readonly Mock<IAuditLogService> _audit = new();
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
            _sut = new SystemHealthController(_jobRuns.Object, _webhooks.Object, _jobs, _audit.Object).WithUser(_adminId, "Admin");
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
            _audit.Verify(a => a.LogAsync(
                "JobRun", "reservationexpiryjob", "RETRY",
                _adminId, It.IsAny<string>(), "Admin",
                null, It.IsAny<object>(), null, It.IsAny<string>()), Times.Once,
                "chạy tay 1 job phải ghi vào audit trail chung, không chỉ nằm trong bảng JobRun riêng");
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
            var id = Guid.NewGuid();
            _webhooks.Setup(s => s.RetryAsync(id)).ReturnsAsync(new WebhookLogDto());

            (await _sut.RetryWebhookLog(id)).StatusOf().Should().Be(200);
            _audit.Verify(a => a.LogAsync(
                "WebhookLog", id.ToString(), "RETRY",
                _adminId, It.IsAny<string>(), "Admin",
                null, It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
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

            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");
            var result = await sut.GetShifts();

            result.StatusOf().Should().Be(200);
            var dto = ((result.Result as ObjectResult)!.Value as List<WarehouseShiftDto>)!.Single();
            dto.StartTime.Should().Be("06:00");
            dto.EndTime.Should().Be("14:30", "TimeSpan phải được format sang hh:mm cho client");
        }

        [Fact]
        public async Task GetShifts_WhenEmpty_ReturnsEmptyList()
        {
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.GetShifts();

            result.StatusOf().Should().Be(200);
            (result.Result as ObjectResult)!.Value.Should().BeAssignableTo<List<WarehouseShiftDto>>()
                .Which.Should().BeEmpty();
        }

        // ── P2-9: CRUD ca làm việc (UC-57) ──────────────────────────────────

        [Fact]
        public async Task CreateShift_Valid_SavesAndReturnsDto()
        {
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.CreateShift(new CreateWarehouseShiftRequest
            {
                Name = "Ca Đêm",
                StartTime = "22:00",
                EndTime = "06:00",
                Description = "ca dem"
            });

            result.StatusOf().Should().Be(200);
            _db.WarehouseShifts.Should().ContainSingle(s => s.Name == "Ca Đêm");
        }

        [Fact]
        public async Task CreateShift_DuplicateName_Returns409()
        {
            _db.WarehouseShifts.Add(new WarehouseShift { Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) });
            await _db.SaveChangesAsync();
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.CreateShift(new CreateWarehouseShiftRequest { Name = "Ca Sáng", StartTime = "06:00", EndTime = "14:00" });

            result.StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateShift_InvalidTimeFormat_Returns400()
        {
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.CreateShift(new CreateWarehouseShiftRequest { Name = "Ca Lạ", StartTime = "not-a-time", EndTime = "14:00" });

            result.StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateShift_DuplicateNameOfAnotherShift_Returns409()
        {
            var s1 = new WarehouseShift { Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) };
            var s2 = new WarehouseShift { Name = "Ca Trưa", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0) };
            _db.WarehouseShifts.AddRange(s1, s2);
            await _db.SaveChangesAsync();
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.UpdateShift(s2.Id, new UpdateWarehouseShiftRequest { Name = "Ca Sáng", StartTime = "14:00", EndTime = "22:00" });

            result.StatusOf().Should().Be(409);
            _db.WarehouseShifts.Single(s => s.Id == s2.Id).Name.Should().Be("Ca Trưa");
        }

        [Fact]
        public async Task UpdateShift_WhenMissing_Returns404()
        {
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.UpdateShift(Guid.NewGuid(), new UpdateWarehouseShiftRequest { Name = "X", StartTime = "06:00", EndTime = "14:00" });

            result.StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task DeleteShift_Existing_RemovesRecord()
        {
            var shift = new WarehouseShift { Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) };
            _db.WarehouseShifts.Add(shift);
            await _db.SaveChangesAsync();
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.DeleteShift(shift.Id);

            result.StatusOf().Should().Be(204);
            _db.WarehouseShifts.Should().BeEmpty();
        }

        [Fact]
        public async Task DeleteShift_WhenMissing_Returns404()
        {
            var sut = new WarehouseShiftController(_db, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

            var result = await sut.DeleteShift(Guid.NewGuid());

            result.StatusOf().Should().Be(404);
        }

        // ── audit log: mọi thao tác CRUD ca làm việc phải ghi lại được ai/khi nào ──

        [Fact]
        public async Task CreateShift_Success_WritesAuditLog()
        {
            var audit = new Mock<IAuditLogService>();
            var actorId = Guid.NewGuid();
            var sut = new WarehouseShiftController(_db, audit.Object).WithUser(actorId, "Admin");

            var result = await sut.CreateShift(new CreateWarehouseShiftRequest { Name = "Ca Chieu", StartTime = "14:00", EndTime = "22:00" });
            var createdId = ((result.Result as ObjectResult)!.Value as WarehouseShiftDto)!.Id;

            audit.Verify(a => a.LogAsync(
                "WarehouseShift", createdId.ToString(), "CREATE",
                actorId, It.IsAny<string>(), It.IsAny<string>(),
                null, It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task UpdateShift_Success_WritesAuditLog()
        {
            var shift = new WarehouseShift { Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) };
            _db.WarehouseShifts.Add(shift);
            await _db.SaveChangesAsync();
            var audit = new Mock<IAuditLogService>();
            var actorId = Guid.NewGuid();
            var sut = new WarehouseShiftController(_db, audit.Object).WithUser(actorId, "Admin");

            await sut.UpdateShift(shift.Id, new UpdateWarehouseShiftRequest { Name = "Ca Sáng Sớm", StartTime = "05:00", EndTime = "13:00" });

            audit.Verify(a => a.LogAsync(
                "WarehouseShift", shift.Id.ToString(), "UPDATE",
                actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), null, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteShift_Success_WritesAuditLog()
        {
            var shift = new WarehouseShift { Name = "Ca Sáng", StartTime = new TimeSpan(6, 0, 0), EndTime = new TimeSpan(14, 0, 0) };
            _db.WarehouseShifts.Add(shift);
            await _db.SaveChangesAsync();
            var audit = new Mock<IAuditLogService>();
            var actorId = Guid.NewGuid();
            var sut = new WarehouseShiftController(_db, audit.Object).WithUser(actorId, "Admin");

            await sut.DeleteShift(shift.Id);

            audit.Verify(a => a.LogAsync(
                "WarehouseShift", shift.Id.ToString(), "DELETE",
                actorId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), null, null, It.IsAny<string>()), Times.Once);
        }
    }
}
