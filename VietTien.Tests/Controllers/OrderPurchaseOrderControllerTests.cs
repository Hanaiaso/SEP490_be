using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.PurchaseOrder;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ OrderController (494 dòng, trước đó 17,9%).</summary>
    public class OrderControllerTests
    {
        private readonly Mock<IOrderService> _service = new();
        private readonly Mock<ICloudinaryService> _cloudinary = new();
        private readonly Guid _userId = Guid.NewGuid();

        private OrderController Build(string role = "Customer")
            => new OrderController(_service.Object, _cloudinary.Object).WithUser(_userId, role);

        private static IFormFile FakeFile(long length = 64)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.SetupGet(f => f.FileName).Returns("evidence.jpg");
            return file.Object;
        }

        // ── checkout ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetCheckoutSummary_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetCheckoutSummaryAsync(_userId, null)).ReturnsAsync(new OrderPreviewDto());

            (await Build().GetCheckoutSummary()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetCheckoutSummary_WhenCartEmpty_Returns404()
        {
            _service.Setup(s => s.GetCheckoutSummaryAsync(It.IsAny<Guid>(), It.IsAny<List<Guid>>()))
                .ThrowsAsync(new KeyNotFoundException("Gio hang trong"));

            (await Build().GetCheckoutSummary()).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetCheckoutSummary_WithoutUserClaim_Returns400()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.GetCheckoutSummary()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task PlaceOrder_WhenModelStateInvalid_Returns400WithoutCallingService()
        {
            var sut = Build().WithInvalidModelState("AddressId", "bat buoc");

            (await sut.PlaceOrder(new PlaceOrderRequestDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.PlaceOrderAsync(It.IsAny<Guid>(), It.IsAny<PlaceOrderRequestDto>()), Times.Never);
        }

        [Fact]
        public async Task PlaceOrder_Success_ReturnsOk()
        {
            _service.Setup(s => s.PlaceOrderAsync(_userId, It.IsAny<PlaceOrderRequestDto>()))
                .ReturnsAsync(new OrderResponseDto());

            (await Build().PlaceOrder(new PlaceOrderRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task PlaceOrder_WhenAddressMissing_Returns404()
        {
            _service.Setup(s => s.PlaceOrderAsync(It.IsAny<Guid>(), It.IsAny<PlaceOrderRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay dia chi"));

            (await Build().PlaceOrder(new PlaceOrderRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PlaceOrder_WhenStockInsufficient_Returns400()
        {
            _service.Setup(s => s.PlaceOrderAsync(It.IsAny<Guid>(), It.IsAny<PlaceOrderRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Ton kho khong du"));

            (await Build().PlaceOrder(new PlaceOrderRequestDto())).StatusOf().Should().Be(400);
        }

        // ── thanh toán SePay ─────────────────────────────────────────────────

        [Fact]
        public async Task GenerateSePayQr_Success_PassesCallerIdentity()
        {
            _service.Setup(s => s.GenerateSePayQrAsync(It.IsAny<Guid>(), _userId, "Customer"))
                .ReturnsAsync(new SePayQrResponseDto());

            (await Build().GenerateSePayQr(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GenerateSePayQr_WhenOrderOfAnotherCustomer_Returns403()
        {
            _service.Setup(s => s.GenerateSePayQrAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().GenerateSePayQr(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GenerateSePayQr_WhenOrderAlreadyPaid_Returns400()
        {
            _service.Setup(s => s.GenerateSePayQrAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Don da thanh toan"));

            (await Build().GenerateSePayQr(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetPaymentStatus_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetPaymentStatusAsync(It.IsAny<Guid>(), _userId, "Customer"))
                .ReturnsAsync(new PaymentStatusResponseDto());

            (await Build().GetPaymentStatus(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetPaymentStatus_WhenOrderOfAnotherCustomer_Returns403()
        {
            _service.Setup(s => s.GetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().GetPaymentStatus(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetPaymentStatus_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetPaymentStatus(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── bán tại quầy (POS) ───────────────────────────────────────────────

        [Fact]
        public async Task PlaceDirectOrder_WhenModelStateInvalid_Returns400()
        {
            var sut = Build("SalesStaff").WithInvalidModelState("Items", "bat buoc");

            (await sut.PlaceDirectOrder(new PlaceDirectOrderRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task PlaceDirectOrder_Success_ReturnsOk()
        {
            _service.Setup(s => s.PlaceDirectOrderAsync(It.IsAny<PlaceDirectOrderRequestDto>(), It.IsAny<Guid>()))
                .ReturnsAsync(new DirectOrderResponseDto());

            (await Build("SalesStaff").PlaceDirectOrder(new PlaceDirectOrderRequestDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task PlaceDirectOrder_WhenProductMissing_Returns404()
        {
            _service.Setup(s => s.PlaceDirectOrderAsync(It.IsAny<PlaceDirectOrderRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").PlaceDirectOrder(new PlaceDirectOrderRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PlaceDirectOrder_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.PlaceDirectOrderAsync(It.IsAny<PlaceDirectOrderRequestDto>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesStaff").PlaceDirectOrder(new PlaceDirectOrderRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ConfirmPayment_Success_ReturnsOk()
        {
            (await Build("SalesStaff").ConfirmPayment(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ConfirmPayment_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.ConfirmDirectOrderPaymentAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").ConfirmPayment(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ConfirmPayment_WhenAlreadyPaid_Returns409()
        {
            _service.Setup(s => s.ConfirmDirectOrderPaymentAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Don da thanh toan"));

            (await Build("SalesStaff").ConfirmPayment(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ConfirmPayment_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ConfirmDirectOrderPaymentAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await Build("SalesStaff").ConfirmPayment(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── dashboard Sales: phạm vi theo vai trò ───────────────────────────

        [Fact]
        public async Task GetSalesDashboard_AsSalesStaff_ScopesToOwnData()
        {
            _service.Setup(s => s.GetSalesDashboardStatsAsync(It.IsAny<Guid?>()))
                .ReturnsAsync(new SalesDashboardStatsDto());

            (await Build("SalesStaff").GetSalesDashboard()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesDashboardStatsAsync(_userId), Times.Once,
                "business.md bước 6: SalesStaff chỉ xem đúng phạm vi của mình");
        }

        [Fact]
        public async Task GetSalesDashboard_AsSalesManager_SeesWholeSystem()
        {
            _service.Setup(s => s.GetSalesDashboardStatsAsync(It.IsAny<Guid?>()))
                .ReturnsAsync(new SalesDashboardStatsDto());

            (await Build("SalesManager").GetSalesDashboard()).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesDashboardStatsAsync(null), Times.Once);
        }

        [Fact]
        public async Task GetSalesDashboard_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetSalesDashboardStatsAsync(It.IsAny<Guid?>())).ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").GetSalesDashboard()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetSalesDashboard_WhenDataMissing_Returns404()
        {
            _service.Setup(s => s.GetSalesDashboardStatsAsync(It.IsAny<Guid?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").GetSalesDashboard()).StatusOf().Should().Be(404);
        }

        // ── hoá đơn PDF ──────────────────────────────────────────────────────

        [Fact]
        public async Task UploadInvoicePdf_WhenModelStateInvalid_Returns400()
        {
            var sut = Build().WithInvalidModelState("PdfBase64", "khong phai PDF");

            (await sut.UploadInvoicePdf(Guid.NewGuid(), new UploadPdfRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadInvoicePdf_Success_ReturnsOk()
        {
            (await Build().UploadInvoicePdf(Guid.NewGuid(), new UploadPdfRequestDto { PdfBase64 = "JVBERi0=" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.UploadInvoicePdfAsync(It.IsAny<Guid>(), "JVBERi0=", _userId, "Customer"), Times.Once);
        }

        [Fact]
        public async Task UploadInvoicePdf_WhenOrderOfAnotherCustomer_Returns403()
        {
            _service.Setup(s => s.UploadInvoicePdfAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build().UploadInvoicePdf(Guid.NewGuid(), new UploadPdfRequestDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task UploadInvoicePdf_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UploadInvoicePdfAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().UploadInvoicePdf(Guid.NewGuid(), new UploadPdfRequestDto())).StatusOf().Should().Be(400);
        }

        // ── lịch sử đơn của khách ────────────────────────────────────────────

        [Fact]
        public async Task GetMyOrderHistory_Success_ScopesToCaller()
        {
            _service.Setup(s => s.GetOrderHistoryAsync(_userId, It.IsAny<OrderHistoryQueryDto>()))
                .ReturnsAsync(new PagedResultDto<OrderHistoryItemDto>());

            (await Build().GetMyOrderHistory(new OrderHistoryQueryDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetOrderHistoryAsync(_userId, It.IsAny<OrderHistoryQueryDto>()), Times.Once);
        }

        [Fact]
        public async Task GetMyOrderHistory_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.GetMyOrderHistory(new OrderHistoryQueryDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetMyOrderHistory_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetOrderHistoryAsync(It.IsAny<Guid>(), It.IsAny<OrderHistoryQueryDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetMyOrderHistory(new OrderHistoryQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetMyOrderDetail_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetOrderDetailForCustomerAsync(_userId, It.IsAny<Guid>()))
                .ReturnsAsync(new OrderHistoryDetailDto());

            (await Build().GetMyOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMyOrderDetail_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.GetOrderDetailForCustomerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().GetMyOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetMyOrderDetail_WhenOrderOfAnotherCustomer_Returns401()
        {
            _service.Setup(s => s.GetOrderDetailForCustomerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException("khong phai don cua ban"));

            (await Build().GetMyOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetMyOrderDetail_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetOrderDetailForCustomerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetMyOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── tra cứu đơn công khai ────────────────────────────────────────────

        [Fact]
        public async Task TrackOrderPublic_Success_ReturnsOk()
        {
            _service.Setup(s => s.TrackOrderPublicAsync("ORD-001")).ReturnsAsync(new OrderHistoryDetailDto());

            (await Build().TrackOrderPublic("ORD-001")).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task TrackOrderPublic_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.TrackOrderPublicAsync(It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().TrackOrderPublic("ORD-999")).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task TrackOrderPublic_WhenQueryBlank_Returns400()
        {
            _service.Setup(s => s.TrackOrderPublicAsync(It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("Vui long nhap ma don"));

            (await Build().TrackOrderPublic("")).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task TrackOrderPublic_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.TrackOrderPublicAsync(It.IsAny<string>())).ThrowsAsync(new Exception("loi"));

            (await Build().TrackOrderPublic("x")).StatusOf().Should().Be(400);
        }

        // ── thống kê chi tiêu + hoá đơn VAT ─────────────────────────────────

        [Fact]
        public async Task GetMySpendingStats_ForwardsPeriod()
        {
            _service.Setup(s => s.GetSpendingStatsAsync(_userId, "month")).ReturnsAsync(new SpendingStatsDto());

            (await Build().GetMySpendingStats(new SpendingStatsQueryDto { Period = "month" }))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMySpendingStats_WithoutUserClaim_Returns401()
        {
            var sut = Build();
            sut.WithAnonymousUser();

            (await sut.GetMySpendingStats(new SpendingStatsQueryDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetMySpendingStats_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetSpendingStatsAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().GetMySpendingStats(new SpendingStatsQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestVatInvoice_Success_ReturnsOk()
        {
            (await Build().RequestVatInvoice(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.RequestVatInvoiceAsync(_userId, It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task RequestVatInvoice_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.RequestVatInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().RequestVatInvoice(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestVatInvoice_WhenProfileMissingTaxCode_Returns400()
        {
            _service.Setup(s => s.RequestVatInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Chua khai MST"));

            (await Build().RequestVatInvoice(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestVatInvoice_WhenOrderOfAnotherCustomer_Returns401()
        {
            _service.Setup(s => s.RequestVatInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException("khong phai don cua ban"));

            (await Build().RequestVatInvoice(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task RequestVatInvoice_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.RequestVatInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().RequestVatInvoice(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── màn hình Sales: danh sách + chi tiết đơn ────────────────────────

        [Fact]
        public async Task GetSalesOrders_AsSalesStaff_ScopesToOwnCustomers()
        {
            _service.Setup(s => s.GetSalesOrdersAsync(It.IsAny<SalesOrderQueryDto>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new PagedResultDto<SalesOrderListDto>());

            (await Build("SalesStaff").GetSalesOrders(new SalesOrderQueryDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesOrdersAsync(It.IsAny<SalesOrderQueryDto>(), _userId), Times.Once,
                "vá IDOR L1-ORD-71: Sales chỉ thấy đơn khách mình phụ trách");
        }

        [Fact]
        public async Task GetSalesOrders_AsSalesManager_SeesAll()
        {
            _service.Setup(s => s.GetSalesOrdersAsync(It.IsAny<SalesOrderQueryDto>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new PagedResultDto<SalesOrderListDto>());

            (await Build("SalesManager").GetSalesOrders(new SalesOrderQueryDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesOrdersAsync(It.IsAny<SalesOrderQueryDto>(), null), Times.Once);
        }

        [Fact]
        public async Task GetSalesOrders_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetSalesOrdersAsync(It.IsAny<SalesOrderQueryDto>(), It.IsAny<Guid?>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").GetSalesOrders(new SalesOrderQueryDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetSalesOrderDetail_AsSalesStaff_ScopesToOwnCustomers()
        {
            _service.Setup(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new SalesOrderDetailDto());

            (await Build("SalesStaff").GetSalesOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), _userId), Times.Once);
        }

        [Fact]
        public async Task GetSalesOrderDetail_AsAdmin_NoScoping()
        {
            _service.Setup(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .ReturnsAsync(new SalesOrderDetailDto());

            (await Build("Admin").GetSalesOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), null), Times.Once);
        }

        [Fact]
        public async Task GetSalesOrderDetail_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").GetSalesOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetSalesOrderDetail_WhenOrderOfAnotherSale_Returns403()
        {
            _service.Setup(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await Build("SalesStaff").GetSalesOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task GetSalesOrderDetail_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.GetSalesOrderDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid?>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").GetSalesOrderDetail(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── duyệt / từ chối đơn ─────────────────────────────────────────────

        [Fact]
        public async Task ConfirmSalesOrder_Success_ReturnsOk()
        {
            (await Build("SalesStaff").ConfirmSalesOrder(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.ConfirmOrderAsync(It.IsAny<Guid>(), _userId), Times.Once);
        }

        [Fact]
        public async Task ConfirmSalesOrder_WhenMissing_Returns404()
        {
            _service.Setup(s => s.ConfirmOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").ConfirmSalesOrder(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ConfirmSalesOrder_WhenWrongState_PropagatesToMiddlewareAs409()
        {
            // GH-09: controller không còn tự bắt InvalidOperationException — để
            // ExceptionHandlingMiddleware map đúng 409 Conflict (trước đây bắt cục bộ trả nhầm 400).
            _service.Setup(s => s.ConfirmOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Don khong o trang thai cho xac nhan"));

            var act = () => Build("SalesStaff").ConfirmSalesOrder(Guid.NewGuid());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task ConfirmSalesOrder_WhenServiceThrows_PropagatesToMiddleware()
        {
            _service.Setup(s => s.ConfirmOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("loi"));

            var act = () => Build("SalesStaff").ConfirmSalesOrder(Guid.NewGuid());
            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task RejectSalesOrder_WhenModelStateInvalid_Returns400()
        {
            var sut = Build("SalesStaff").WithInvalidModelState("Reason", "bat buoc");

            (await sut.RejectSalesOrder(Guid.NewGuid(), new RejectOrderRequestDto())).StatusOf().Should().Be(400);
            _service.Verify(s => s.RejectOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RejectSalesOrder_Success_ForwardsReason()
        {
            (await Build("SalesStaff").RejectSalesOrder(Guid.NewGuid(), new RejectOrderRequestDto { Reason = "het hang" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.RejectOrderAsync(It.IsAny<Guid>(), _userId, "het hang"), Times.Once);
        }

        [Fact]
        public async Task RejectSalesOrder_WhenMissing_Returns404()
        {
            _service.Setup(s => s.RejectOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesStaff").RejectSalesOrder(Guid.NewGuid(), new RejectOrderRequestDto()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RejectSalesOrder_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.RejectOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await Build("SalesStaff").RejectSalesOrder(Guid.NewGuid(), new RejectOrderRequestDto()))
                .StatusOf().Should().Be(400);
        }

        // ── yêu cầu / xử lý huỷ đơn ─────────────────────────────────────────

        [Fact]
        public async Task RequestCancelOrder_WhenModelStateInvalid_Returns400()
        {
            var sut = Build().WithInvalidModelState("Reason", "bat buoc");

            (await sut.RequestCancelOrder(Guid.NewGuid(), new RequestCancelOrderDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestCancelOrder_Success_ReturnsOk()
        {
            (await Build().RequestCancelOrder(Guid.NewGuid(), new RequestCancelOrderDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.RequestCancelOrderAsync(It.IsAny<Guid>(), _userId, It.IsAny<RequestCancelOrderDto>()),
                Times.Once);
        }

        [Fact]
        public async Task RequestCancelOrder_WhenMissing_Returns404()
        {
            _service.Setup(s => s.RequestCancelOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RequestCancelOrderDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().RequestCancelOrder(Guid.NewGuid(), new RequestCancelOrderDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestCancelOrder_WhenOrderAlreadyShipped_Returns400()
        {
            _service.Setup(s => s.RequestCancelOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RequestCancelOrderDto>()))
                .ThrowsAsync(new InvalidOperationException("Don dang giao, khong the huy"));

            (await Build().RequestCancelOrder(Guid.NewGuid(), new RequestCancelOrderDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestCancelOrder_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.RequestCancelOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RequestCancelOrderDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().RequestCancelOrder(Guid.NewGuid(), new RequestCancelOrderDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessCancelRequest_WhenModelStateInvalid_Returns400()
        {
            var sut = Build("SalesManager").WithInvalidModelState("Decision", "bat buoc");

            (await sut.ProcessCancelRequest(Guid.NewGuid(), new ProcessCancelRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessCancelRequest_Success_ReturnsOk()
        {
            (await Build("SalesManager").ProcessCancelRequest(Guid.NewGuid(), new ProcessCancelRequestDto()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ProcessCancelRequest_WhenMissing_Returns404()
        {
            _service.Setup(s => s.ProcessCancelRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProcessCancelRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").ProcessCancelRequest(Guid.NewGuid(), new ProcessCancelRequestDto()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ProcessCancelRequest_WhenNoPendingRequest_Returns400()
        {
            _service.Setup(s => s.ProcessCancelRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProcessCancelRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Khong co yeu cau huy dang cho"));

            (await Build("SalesManager").ProcessCancelRequest(Guid.NewGuid(), new ProcessCancelRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessCancelRequest_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ProcessCancelRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProcessCancelRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").ProcessCancelRequest(Guid.NewGuid(), new ProcessCancelRequestDto()))
                .StatusOf().Should().Be(400);
        }

        // ── đổi / trả hàng ───────────────────────────────────────────────────

        [Fact]
        public async Task CreateReturnExchangeRequest_WhenModelStateInvalid_Returns400()
        {
            var sut = Build().WithInvalidModelState("Items", "bat buoc");

            (await sut.CreateReturnExchangeRequest(Guid.NewGuid(), new CreateReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateReturnExchangeRequest_Success_ReturnsOk()
        {
            (await Build().CreateReturnExchangeRequest(Guid.NewGuid(), new CreateReturnExchangeRequestDto()))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.CreateReturnExchangeRequestAsync(It.IsAny<Guid>(), _userId,
                It.IsAny<CreateReturnExchangeRequestDto>()), Times.Once);
        }

        [Fact]
        public async Task CreateReturnExchangeRequest_WhenOrderMissing_Returns404()
        {
            _service.Setup(s => s.CreateReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<CreateReturnExchangeRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build().CreateReturnExchangeRequest(Guid.NewGuid(), new CreateReturnExchangeRequestDto()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateReturnExchangeRequest_WhenPastReturnWindow_Returns400()
        {
            _service.Setup(s => s.CreateReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<CreateReturnExchangeRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Qua thoi han doi tra"));

            (await Build().CreateReturnExchangeRequest(Guid.NewGuid(), new CreateReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateReturnExchangeRequest_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.CreateReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<CreateReturnExchangeRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build().CreateReturnExchangeRequest(Guid.NewGuid(), new CreateReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessReturnExchangeRequest_WhenModelStateInvalid_Returns400()
        {
            var sut = Build("SalesManager").WithInvalidModelState("Decision", "bat buoc");

            (await sut.ProcessReturnExchangeRequest(Guid.NewGuid(), new ProcessReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessReturnExchangeRequest_Success_ReturnsOk()
        {
            (await Build("SalesManager").ProcessReturnExchangeRequest(Guid.NewGuid(), new ProcessReturnExchangeRequestDto()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ProcessReturnExchangeRequest_WhenMissing_Returns404()
        {
            _service.Setup(s => s.ProcessReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<ProcessReturnExchangeRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await Build("SalesManager").ProcessReturnExchangeRequest(Guid.NewGuid(), new ProcessReturnExchangeRequestDto()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ProcessReturnExchangeRequest_WhenAlreadyProcessed_Returns400()
        {
            _service.Setup(s => s.ProcessReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<ProcessReturnExchangeRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Da xu ly truoc do"));

            (await Build("SalesManager").ProcessReturnExchangeRequest(Guid.NewGuid(), new ProcessReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ProcessReturnExchangeRequest_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.ProcessReturnExchangeRequestAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<ProcessReturnExchangeRequestDto>()))
                .ThrowsAsync(new Exception("loi"));

            (await Build("SalesManager").ProcessReturnExchangeRequest(Guid.NewGuid(), new ProcessReturnExchangeRequestDto()))
                .StatusOf().Should().Be(400);
        }

        // ── upload ảnh bằng chứng ────────────────────────────────────────────

        [Fact]
        public async Task UploadEvidence_WhenNoFile_Returns400WithoutCallingCloudinary()
        {
            (await Build().UploadEvidence(null!)).StatusOf().Should().Be(400);
            _cloudinary.Verify(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UploadEvidence_WhenFileEmpty_Returns400()
        {
            (await Build().UploadEvidence(FakeFile(length: 0))).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadEvidence_Success_ReturnsUrl()
        {
            _cloudinary.Setup(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), "viettien/return-exchange-evidence"))
                .ReturnsAsync("https://cdn/evidence.jpg");

            var result = await Build().UploadEvidence(FakeFile());

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value!.ToString().Should().Contain("https://cdn/evidence.jpg");
        }

        [Fact]
        public async Task UploadEvidence_WhenUploadFails_Returns400()
        {
            _cloudinary.Setup(c => c.UploadEvidenceAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("cloudinary loi"));

            (await Build().UploadEvidence(FakeFile())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>
    /// Case code-driven phủ PurchaseOrderController (385 dòng, trước đó 19,1%).
    /// Endpoint `GetWarehouses` truy vấn thẳng ApplicationDbContext nên dùng EF InMemory,
    /// phần còn lại mock service.
    /// </summary>
    public class PurchaseOrderControllerTests
    {
        private readonly Mock<IPurchaseOrderService> _po = new();
        private readonly Mock<IGoodsReceiptService> _receipt = new();
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly PurchaseOrderController _sut;
        private readonly Guid _ceoId = Guid.NewGuid();

        public PurchaseOrderControllerTests()
            => _sut = new PurchaseOrderController(_po.Object, _receipt.Object, _db).WithUser(_ceoId, "CEO");

        private static IFormFile FakeFile(long length = 64, string name = "po.xlsx")
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.SetupGet(f => f.FileName).Returns(name);
            return file.Object;
        }

        [Fact]
        public async Task GetWarehouses_ProjectsIdNameCodeOnly()
        {
            _db.Warehouses.Add(new Warehouse { Name = "Kho Hà Nội", Code = "WH-HN" });
            await _db.SaveChangesAsync();

            var result = await _sut.GetWarehouses();

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value.Should().BeAssignableTo<IEnumerable<object>>()
                .Which.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetWarehouses_WhenEmpty_ReturnsEmptyList()
        {
            var result = await _sut.GetWarehouses();

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value.Should().BeAssignableTo<IEnumerable<object>>().Which.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAll_ForwardsStatusFilter()
        {
            _po.Setup(s => s.GetAllAsync("Draft")).ReturnsAsync(new List<PurchaseOrderListDto>());

            (await _sut.GetAll("Draft")).StatusOf().Should().Be(200);
            _po.Verify(s => s.GetAllAsync("Draft"), Times.Once);
        }

        [Fact]
        public async Task GetAll_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.GetAllAsync(It.IsAny<string?>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetAll(null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetById_Success_ReturnsOk()
        {
            _po.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetById_WhenMissing_Returns404()
        {
            _po.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetById_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetById(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_Success_PassesCeoIdFromToken()
        {
            _po.Setup(s => s.CreateAsync(_ceoId, It.IsAny<CreatePurchaseOrderRequest>()))
                .ReturnsAsync(new PurchaseOrderDto());

            (await _sut.Create(new CreatePurchaseOrderRequest())).StatusOf().Should().Be(200);
            _po.Verify(s => s.CreateAsync(_ceoId, It.IsAny<CreatePurchaseOrderRequest>()), Times.Once);
        }

        [Fact]
        public async Task Create_WhenSupplierMissing_Returns400()
        {
            _po.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay nha cung cap"));

            // Action này chỉ có catch(Exception) -> KeyNotFound cũng thành 400.
            (await _sut.Create(new CreatePurchaseOrderRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ImportFromExcel_Success_ReturnsOk()
        {
            _po.Setup(s => s.ImportFromExcelAsync(It.IsAny<IFormFile>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.ImportFromExcel(FakeFile())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ImportFromExcel_WhenFileMalformed_Returns400()
        {
            _po.Setup(s => s.ImportFromExcelAsync(It.IsAny<IFormFile>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("File khong dung dinh dang"));

            (await _sut.ImportFromExcel(FakeFile())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ImportFromImage_Success_ReturnsOk()
        {
            _po.Setup(s => s.ImportFromImageAsync(It.IsAny<IFormFile>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.ImportFromImage(FakeFile(name: "po.jpg"))).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ImportFromImage_WhenOcrFails_Returns400()
        {
            _po.Setup(s => s.ImportFromImageAsync(It.IsAny<IFormFile>(), It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("OCR that bai"));

            (await _sut.ImportFromImage(FakeFile(name: "po.jpg"))).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateDraft_Success_ReturnsOk()
        {
            _po.Setup(s => s.UpdateDraftAsync(It.IsAny<Guid>(), _ceoId, It.IsAny<CreatePurchaseOrderRequest>()))
                .ReturnsAsync(new PurchaseOrderDto());

            (await _sut.UpdateDraft(Guid.NewGuid(), new CreatePurchaseOrderRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateDraft_WhenMissing_Returns404()
        {
            _po.Setup(s => s.UpdateDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.UpdateDraft(Guid.NewGuid(), new CreatePurchaseOrderRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateDraft_WhenAlreadyIssued_Returns409()
        {
            _po.Setup(s => s.UpdateDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>()))
                .ThrowsAsync(new InvalidOperationException("PO da phat hanh, khong sua duoc"));

            (await _sut.UpdateDraft(Guid.NewGuid(), new CreatePurchaseOrderRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdateDraft_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.UpdateDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.UpdateDraft(Guid.NewGuid(), new CreatePurchaseOrderRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UpdateDraft_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.UpdateDraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreatePurchaseOrderRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.UpdateDraft(Guid.NewGuid(), new CreatePurchaseOrderRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Issue_Success_ReturnsOk()
        {
            _po.Setup(s => s.IssueAsync(It.IsAny<Guid>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.Issue(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Issue_WhenMissing_Returns404()
        {
            _po.Setup(s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Issue(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Issue_WhenNotDraft_Returns409()
        {
            _po.Setup(s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Chi phat hanh duoc PO Nhap"));

            (await _sut.Issue(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Issue_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.Issue(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Issue_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.Issue(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SendToWarehouse_Success_ReturnsOk()
        {
            _po.Setup(s => s.SendToWarehouseAsync(It.IsAny<Guid>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.SendToWarehouse(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SendToWarehouse_WhenMissing_Returns404()
        {
            _po.Setup(s => s.SendToWarehouseAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.SendToWarehouse(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task SendToWarehouse_WhenNotIssued_Returns409()
        {
            _po.Setup(s => s.SendToWarehouseAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("PO chua phat hanh"));

            (await _sut.SendToWarehouse(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task SendToWarehouse_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.SendToWarehouseAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.SendToWarehouse(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task SendToWarehouse_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.SendToWarehouseAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.SendToWarehouse(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Cancel_Success_ReturnsOk()
        {
            _po.Setup(s => s.CancelAsync(It.IsAny<Guid>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Cancel_WhenMissing_Returns404()
        {
            _po.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Cancel_WhenGoodsAlreadyReceived_Returns409()
        {
            _po.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Da nhan hang, khong the huy"));

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Cancel_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Cancel_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ResolveDiscrepancy_Success_ReturnsOk()
        {
            _po.Setup(s => s.ResolveDiscrepancyAsync(It.IsAny<Guid>(), _ceoId, It.IsAny<DiscrepancyResolutionRequest>()))
                .ReturnsAsync(new PurchaseOrderDto());

            (await _sut.ResolveDiscrepancy(Guid.NewGuid(), new DiscrepancyResolutionRequest()))
                .StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task ResolveDiscrepancy_WhenMissing_Returns404()
        {
            _po.Setup(s => s.ResolveDiscrepancyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DiscrepancyResolutionRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.ResolveDiscrepancy(Guid.NewGuid(), new DiscrepancyResolutionRequest()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task ResolveDiscrepancy_WhenNoDiscrepancy_Returns409()
        {
            _po.Setup(s => s.ResolveDiscrepancyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DiscrepancyResolutionRequest>()))
                .ThrowsAsync(new InvalidOperationException("Khong co chenh lech can xu ly"));

            (await _sut.ResolveDiscrepancy(Guid.NewGuid(), new DiscrepancyResolutionRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ResolveDiscrepancy_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.ResolveDiscrepancyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DiscrepancyResolutionRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.ResolveDiscrepancy(Guid.NewGuid(), new DiscrepancyResolutionRequest()))
                .StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task ResolveDiscrepancy_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.ResolveDiscrepancyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DiscrepancyResolutionRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.ResolveDiscrepancy(Guid.NewGuid(), new DiscrepancyResolutionRequest()))
                .StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Close_Success_ReturnsOk()
        {
            _po.Setup(s => s.ClosePurchaseOrderAsync(It.IsAny<Guid>(), _ceoId)).ReturnsAsync(new PurchaseOrderDto());

            (await _sut.Close(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Close_WhenMissing_Returns404()
        {
            _po.Setup(s => s.ClosePurchaseOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Close(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Close_WhenStillHasOpenLines_Returns409()
        {
            _po.Setup(s => s.ClosePurchaseOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Con dong hang chua nhan du"));

            (await _sut.Close(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Close_WhenConcurrent_Returns409()
        {
            _po.Setup(s => s.ClosePurchaseOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.Close(Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task Close_WhenServiceThrows_Returns400()
        {
            _po.Setup(s => s.ClosePurchaseOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.Close(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        // ── phiếu nhận hàng ──────────────────────────────────────────────────

        [Fact]
        public async Task GetAllReceipts_ForwardsStatusFilter()
        {
            _receipt.Setup(s => s.GetAllAsync("Draft")).ReturnsAsync(new List<GoodsReceiptDto>());

            (await _sut.GetAllReceipts("Draft")).StatusOf().Should().Be(200);
            _receipt.Verify(s => s.GetAllAsync("Draft"), Times.Once);
        }

        [Fact]
        public async Task GetAllReceipts_WhenServiceThrows_Returns400()
        {
            _receipt.Setup(s => s.GetAllAsync(It.IsAny<string?>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetAllReceipts(null)).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetReceipts_Success_ReturnsOk()
        {
            _receipt.Setup(s => s.GetByPurchaseOrderIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new List<GoodsReceiptDto>());

            (await _sut.GetReceipts(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetReceipts_WhenPurchaseOrderMissing_Returns404()
        {
            _receipt.Setup(s => s.GetByPurchaseOrderIdAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetReceipts(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetReceipts_WhenServiceThrows_Returns400()
        {
            _receipt.Setup(s => s.GetByPurchaseOrderIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.GetReceipts(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateReceipt_Success_PassesStaffIdFromToken()
        {
            _receipt.Setup(s => s.CreateFromPOAsync(It.IsAny<Guid>(), _ceoId, It.IsAny<CreateGoodsReceiptRequest>()))
                .ReturnsAsync(new GoodsReceiptDto());

            (await _sut.CreateReceipt(Guid.NewGuid(), new CreateGoodsReceiptRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task CreateReceipt_WhenPurchaseOrderMissing_Returns404()
        {
            _receipt.Setup(s => s.CreateFromPOAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateGoodsReceiptRequest>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.CreateReceipt(Guid.NewGuid(), new CreateGoodsReceiptRequest())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task CreateReceipt_WhenPurchaseOrderNotSentToWarehouse_Returns409()
        {
            _receipt.Setup(s => s.CreateFromPOAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateGoodsReceiptRequest>()))
                .ThrowsAsync(new InvalidOperationException("PO chua chuyen kho"));

            (await _sut.CreateReceipt(Guid.NewGuid(), new CreateGoodsReceiptRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateReceipt_WhenConcurrent_Returns409()
        {
            _receipt.Setup(s => s.CreateFromPOAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateGoodsReceiptRequest>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.CreateReceipt(Guid.NewGuid(), new CreateGoodsReceiptRequest())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task CreateReceipt_WhenServiceThrows_Returns400()
        {
            _receipt.Setup(s => s.CreateFromPOAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CreateGoodsReceiptRequest>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.CreateReceipt(Guid.NewGuid(), new CreateGoodsReceiptRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadReceiptProof_Success_ReturnsOk()
        {
            _receipt.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ReturnsAsync(new GoodsReceiptDto());

            (await _sut.UploadReceiptProof(Guid.NewGuid(), Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UploadReceiptProof_WhenReceiptMissing_Returns404()
        {
            _receipt.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.UploadReceiptProof(Guid.NewGuid(), Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UploadReceiptProof_WhenReceiptAlreadyPosted_Returns409()
        {
            _receipt.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new InvalidOperationException("Phieu da ghi so"));

            (await _sut.UploadReceiptProof(Guid.NewGuid(), Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UploadReceiptProof_WhenConcurrent_Returns409()
        {
            _receipt.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.UploadReceiptProof(Guid.NewGuid(), Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task UploadReceiptProof_WhenServiceThrows_Returns400()
        {
            _receipt.Setup(s => s.UploadProofAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new Exception("loi"));

            (await _sut.UploadReceiptProof(Guid.NewGuid(), Guid.NewGuid(), FakeFile())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task PostReceipt_Success_ReturnsOk()
        {
            _receipt.Setup(s => s.PostReceiptAsync(It.IsAny<Guid>(), _ceoId)).ReturnsAsync(new GoodsReceiptDto());

            (await _sut.PostReceipt(Guid.NewGuid(), Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task PostReceipt_WhenMissing_Returns404()
        {
            _receipt.Setup(s => s.PostReceiptAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.PostReceipt(Guid.NewGuid(), Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task PostReceipt_WhenAlreadyPosted_Returns409()
        {
            _receipt.Setup(s => s.PostReceiptAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Phieu da ghi so"));

            (await _sut.PostReceipt(Guid.NewGuid(), Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PostReceipt_WhenConcurrent_Returns409()
        {
            _receipt.Setup(s => s.PostReceiptAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            (await _sut.PostReceipt(Guid.NewGuid(), Guid.NewGuid())).StatusOf().Should().Be(409);
        }

        [Fact]
        public async Task PostReceipt_WhenServiceThrows_Returns400()
        {
            _receipt.Setup(s => s.PostReceiptAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ThrowsAsync(new Exception("loi"));

            (await _sut.PostReceipt(Guid.NewGuid(), Guid.NewGuid())).StatusOf().Should().Be(400);
        }
    }
}
