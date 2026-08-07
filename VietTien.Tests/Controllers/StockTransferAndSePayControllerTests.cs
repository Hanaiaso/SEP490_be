using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.SePay;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>
    /// Case code-driven phủ StockTransferController (210 dòng, trước đó 0%).
    /// Đặc biệt phủ nhánh `catch (DbUpdateConcurrencyException) -> 409` — nhánh này tồn tại ở
    /// 4 action nhưng KHÔNG BAO GIỜ chạy được qua HTTP thật vì `Inventories` không có RowVersion
    /// (xem GH-04), nên mock service là cách duy nhất chạm tới.
    /// </summary>
    public class StockTransferControllerTests
    {
        private readonly Mock<IStockTransferService> _service = new();
        private readonly StockTransferController _sut;
        private readonly Guid _staffId = Guid.NewGuid();
        private readonly Guid _id = Guid.NewGuid();

        public StockTransferControllerTests()
            => _sut = new StockTransferController(_service.Object).WithUser(_staffId, "WarehouseStaff");

        [Fact]
        public async Task GetAll_ReturnsOk()
        {
            _service.Setup(s => s.GetAllAsync())
                .ReturnsAsync(new List<StockTransferDto> { new() { Code = "TR-1" } });

            (await _sut.GetAll()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetAll_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetAllAsync()).ThrowsAsync(new Exception("db loi"));

            (await _sut.GetAll()).StatusOf().Should().Be(400);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task GetById_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.GetById(_id)).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Create_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.Create(new CreateStockTransferDto())).StatusOf().Should().Be(401);
            _service.Verify(s => s.CreateAsync(It.IsAny<CreateStockTransferDto>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Create_Success_PassesCallerIdToService()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateStockTransferDto>(), _staffId))
                .ReturnsAsync(new StockTransferDto { Code = "TR-9" });

            // Create trả 201 CreatedAtAction, khác các action còn lại (200 Ok).
            (await _sut.Create(new CreateStockTransferDto())).StatusOf().Should().Be(201);
            _service.Verify(s => s.CreateAsync(It.IsAny<CreateStockTransferDto>(), _staffId), Times.Once,
                "phiếu phải gắn đúng người tạo lấy từ JWT");
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task Create_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateStockTransferDto>(), It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Create(new CreateStockTransferDto())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Update_WhenConcurrencyConflict_Returns409()
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateStockTransferDto>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            var result = await _sut.Update(_id, new UpdateStockTransferDto());

            result.StatusOf().Should().Be(409);
            result.Should().BeAssignableTo<ObjectResult>().Which.Value!.ToString()
                .Should().Contain("tải lại");
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task Update_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateStockTransferDto>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.Update(_id, new UpdateStockTransferDto())).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Dispatch_Success_ReturnsOk()
        {
            _service.Setup(s => s.DispatchAsync(_id)).ReturnsAsync(new StockTransferDto { Code = "TR-1" });

            (await _sut.DispatchTransfer(_id)).StatusOf().Should().Be(200);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(DbUpdateConcurrencyException), 409)]
        [InlineData(typeof(Exception), 400)]
        public async Task Dispatch_MapsExceptionToStatus(Type exType, int expected)
        {
            var ex = exType == typeof(DbUpdateConcurrencyException)
                ? new DbUpdateConcurrencyException()
                : (Exception)Activator.CreateInstance(exType, "loi")!;
            _service.Setup(s => s.DispatchAsync(It.IsAny<Guid>())).ThrowsAsync(ex);

            (await _sut.DispatchTransfer(_id)).StatusOf().Should().Be(expected);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), 404)]
        [InlineData(typeof(Exception), 400)]
        public async Task Cancel_MapsExceptionToStatus(Type exType, int expected)
        {
            _service.Setup(s => s.CancelAsync(It.IsAny<Guid>()))
                .ThrowsAsync((Exception)Activator.CreateInstance(exType, "loi")!);

            (await _sut.CancelTransfer(_id)).StatusOf().Should().Be(expected);
        }

        [Fact]
        public async Task Cancel_Success_ReturnsOk()
        {
            _service.Setup(s => s.CancelAsync(_id)).ReturnsAsync(new StockTransferDto { Code = "TR-1" });

            (await _sut.CancelTransfer(_id)).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task RequestTransport_Success_ReturnsOk()
        {
            _service.Setup(s => s.RequestTransportAsync(_id)).ReturnsAsync(new StockTransferDto { Code = "TR-1" });

            (await _sut.RequestTransport(_id)).StatusOf().Should().Be(200);
        }
    }

    /// <summary>
    /// Case code-driven phủ SePayController (90 dòng, trước đó 0%).
    /// Controller này rút token theo 3 nguồn ưu tiên: header `x-sepay-token` → header
    /// `Authorization` (có/không tiền tố Bearer) → query `?token=`. Đây là logic bảo mật nên
    /// từng nhánh cần được chạm.
    ///
    /// ⚠ KHÔNG test trường hợp thiếu token: kết quả phụ thuộc biến môi trường TIẾN TRÌNH
    /// ASPNETCORE_ENVIRONMENT (nhánh bypass GH-01) nên sẽ khác nhau giữa các máy.
    /// Trường hợp đó đã có L2-PAY-10/11 chạy trong môi trường được kiểm soát.
    /// </summary>
    public class SePayControllerTests
    {
        private readonly Mock<IOrderService> _orderService = new();
        private readonly Mock<IWebhookLogService> _webhookLog = new();
        private readonly SePayController _sut;

        public SePayControllerTests()
        {
            _webhookLog.Setup(w => w.LogReceivedAsync(It.IsAny<SePayWebhookDto>())).ReturnsAsync(Guid.NewGuid());
            _sut = new SePayController(_orderService.Object, _webhookLog.Object).WithUser();
        }

        private static SePayWebhookDto Payload() => new()
        {
            gateway = "TPBank", transferAmount = 1_000_000m,
            content = "VT123456789", transferContent = "VT123456789",
            referenceCode = "FT1", referenceNumber = "FT1"
        };

        [Fact]
        public async Task Webhook_WithTokenHeader_ProcessesAndMarksLogProcessed()
        {
            _sut.WithHeader("x-sepay-token", "token-dung");

            var result = await _sut.Webhook(Payload());

            result.StatusOf().Should().Be(200);
            _orderService.Verify(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), "token-dung"), Times.Once);
            _webhookLog.Verify(w => w.MarkProcessedAsync(It.IsAny<Guid>()), Times.Once,
                "xử lý thành công phải đánh dấu log Processed");
        }

        [Fact]
        public async Task Webhook_ReadsTokenFromAuthorizationHeaderWithBearerPrefix()
        {
            _sut.WithHeader("Authorization", "Bearer token-bearer");

            await _sut.Webhook(Payload());

            _orderService.Verify(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), "token-bearer"),
                Times.Once, "phải cắt tiền tố Bearer trước khi so token");
        }

        [Fact]
        public async Task Webhook_ReadsTokenFromAuthorizationHeaderWithoutPrefix()
        {
            _sut.WithHeader("Authorization", "token-tran");

            await _sut.Webhook(Payload());

            _orderService.Verify(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), "token-tran"), Times.Once);
        }

        [Fact]
        public async Task Webhook_ReadsTokenFromQueryStringWhenHeadersAbsent()
        {
            _sut.WithQuery("token", "token-query");

            await _sut.Webhook(Payload());

            _orderService.Verify(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), "token-query"), Times.Once);
        }

        [Fact]
        public async Task Webhook_WhenTokenInvalid_Returns401AndDoesNotMarkFailed()
        {
            _sut.WithHeader("x-sepay-token", "token-sai");
            _orderService.Setup(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException("Token không hợp lệ."));

            var result = await _sut.Webhook(Payload());

            result.StatusOf().Should().Be(401);
            _webhookLog.Verify(w => w.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<Exception>()), Times.Never,
                "sai token KHÔNG được đưa vào diện tự động retry — retry dùng token tin cậy sẽ hợp thức hoá ngược request sai");
        }

        [Fact]
        public async Task Webhook_WhenProcessingFails_MarksLogFailedAndReturns500()
        {
            _sut.WithHeader("x-sepay-token", "token-dung");
            _orderService.Setup(o => o.ProcessSePayWebhookAsync(It.IsAny<SePayWebhookDto>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("db sap"));

            var result = await _sut.Webhook(Payload());

            result.StatusOf().Should().Be(500);
            _webhookLog.Verify(w => w.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<Exception>()), Times.Once,
                "lỗi xử lý phải được ghi lại để job retry nhặt lên");
        }
    }
}
