using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.DTOs.Payment;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>
    /// Sheet: ManualPaymentService — case code-driven phủ PaymentsController (159 dòng, trước đó 0%).
    ///
    /// Controller này map exception sang status theo hợp đồng MGR-05:
    ///   KeyNotFoundException -> 404 · ArgumentException -> 400 · InvalidOperationException -> 409
    ///   UnauthorizedAccessException -> 401 · Exception -> 500 INTERNAL_ERROR
    ///   (riêng GetSePayExceptions: Exception -> 400 INTERNAL_ERROR)
    /// và tách "MÃ_LỖI: thông điệp" thành code + message riêng.
    /// </summary>
    public class PaymentsControllerTests
    {
        private readonly Mock<IManualPaymentService> _service = new();
        private readonly PaymentsController _sut;
        private readonly Guid _managerId = Guid.NewGuid();
        private readonly Guid _orderId = Guid.NewGuid();

        public PaymentsControllerTests()
        {
            _sut = new PaymentsController(_service.Object).WithUser(_managerId, "SalesManager");
        }

        private static ManualConfirmPaymentRequest ValidRequest() => new()
        {
            ExternalTransactionId = "FT123456",
            ActualAmount = 1_000_000m,
            EvidenceUrl = "https://example.local/proof.jpg"
        };

        // ── GetSePayExceptions ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetSePayExceptions_ReturnsOkWithList()
        {
            var expected = new List<SePayExceptionItemDto> { new() { OrderId = _orderId, OrderCode = "VT1" } };
            _service.Setup(s => s.GetExceptionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

            var result = await _sut.GetSePayExceptions(CancellationToken.None);

            result.StatusOf().Should().Be(200);
            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeAssignableTo<List<SePayExceptionItemDto>>()
                .Which.Should().ContainSingle(x => x.OrderCode == "VT1");
        }

        [Fact]
        public async Task GetSePayExceptions_WhenServiceThrows_Returns400WithInternalErrorCode()
        {
            _service.Setup(s => s.GetExceptionsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("db sap"));

            var result = await _sut.GetSePayExceptions(CancellationToken.None);

            result.StatusOf().Should().Be(400);
            result.Should().BeOfType<BadRequestObjectResult>()
                .Which.Value!.ToString().Should().Contain("INTERNAL_ERROR");
        }

        // ── ManualConfirm ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ManualConfirm_Success_ReturnsOkWithResponse()
        {
            var response = new ManualConfirmPaymentResponse
            {
                OrderId = _orderId, OrderCode = "VT1", PaymentStatus = "Paid", AllocationStatus = "ALLOCATED"
            };
            _service.Setup(s => s.ManualConfirmAsync(_orderId, It.IsAny<ManualConfirmPaymentRequest>(),
                    _managerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(200);
            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task ManualConfirm_WhenModelStateInvalid_Returns400WithoutCallingService()
        {
            _sut.WithInvalidModelState(nameof(ManualConfirmPaymentRequest.EvidenceUrl), "bat buoc");

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(400);
            _service.Verify(s => s.ManualConfirmAsync(It.IsAny<Guid>(), It.IsAny<ManualConfirmPaymentRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
                "ModelState hỏng thì không được chạm tới service");
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), "PAYMENT_NOT_FOUND: khong tim thay", 404, "PAYMENT_NOT_FOUND")]
        [InlineData(typeof(ArgumentException), "MANUAL_CONFIRM_EVIDENCE_REQUIRED: thieu bang chung", 400, "MANUAL_CONFIRM_EVIDENCE_REQUIRED")]
        [InlineData(typeof(InvalidOperationException), "PAYMENT_ALREADY_CONFIRMED: da xac nhan", 409, "PAYMENT_ALREADY_CONFIRMED")]
        public async Task ManualConfirm_MapsServiceExceptionToStatusAndCode(
            Type exceptionType, string message, int expectedStatus, string expectedCode)
        {
            var ex = (Exception)Activator.CreateInstance(exceptionType, message)!;
            _service.Setup(s => s.ManualConfirmAsync(It.IsAny<Guid>(), It.IsAny<ManualConfirmPaymentRequest>(),
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(expectedStatus);
            result.Should().BeAssignableTo<ObjectResult>().Which.Value!.ToString()
                .Should().Contain(expectedCode, "controller phải tách mã lỗi ra khỏi thông điệp");
        }

        [Fact]
        public async Task ManualConfirm_WhenUnauthorized_Returns401()
        {
            _service.Setup(s => s.ManualConfirmAsync(It.IsAny<Guid>(), It.IsAny<ManualConfirmPaymentRequest>(),
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("khong co quyen"));

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task ManualConfirm_WhenUnexpectedException_Returns500()
        {
            _service.Setup(s => s.ManualConfirmAsync(It.IsAny<Guid>(), It.IsAny<ManualConfirmPaymentRequest>(),
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("loi la"));

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(500);
        }

        [Fact]
        public async Task ManualConfirm_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            var result = await _sut.ManualConfirm(_orderId, ValidRequest(), CancellationToken.None);

            result.StatusOf().Should().Be(401, "GetUserId() ném UnauthorizedAccessException -> controller trả Unauthorized");
            _service.Verify(s => s.ManualConfirmAsync(It.IsAny<Guid>(), It.IsAny<ManualConfirmPaymentRequest>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── RetryAllocation ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task RetryAllocation_Success_ReturnsOkAndPassesNoteThrough()
        {
            var response = new ManualConfirmPaymentResponse { OrderId = _orderId, AllocationStatus = "ALLOCATED" };
            _service.Setup(s => s.RetryAllocationAsync(_orderId, _managerId, "thu lai", It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await _sut.RetryAllocation(_orderId,
                new RetryAllocationRequest { Note = "thu lai" }, CancellationToken.None);

            result.StatusOf().Should().Be(200);
            _service.Verify(s => s.RetryAllocationAsync(_orderId, _managerId, "thu lai",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(typeof(KeyNotFoundException), "PAYMENT_NOT_FOUND: khong thay", 404)]
        [InlineData(typeof(InvalidOperationException), "ORDER_NOT_PAID: chua tra tien", 409)]
        [InlineData(typeof(UnauthorizedAccessException), "khong co quyen", 401)]
        [InlineData(typeof(Exception), "loi la", 500)]
        public async Task RetryAllocation_MapsServiceExceptionToStatus(
            Type exceptionType, string message, int expectedStatus)
        {
            var ex = (Exception)Activator.CreateInstance(exceptionType, message)!;
            _service.Setup(s => s.RetryAllocationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);

            var result = await _sut.RetryAllocation(_orderId,
                new RetryAllocationRequest { Note = null }, CancellationToken.None);

            result.StatusOf().Should().Be(expectedStatus);
        }
    }
}
