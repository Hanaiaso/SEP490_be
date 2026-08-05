using FluentAssertions;
using VietTien.API.DTOs.Order;
using VietTien.API.Models;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: RegressionFixes — phần thuộc OrderService (L1-REG-06, L1-REG-07), commit 510915c.
    /// Đặt ở đây để dùng chung fixture OrderServiceTests thay vì dựng lại toàn bộ dependency.
    /// </summary>
    public partial class OrderServiceTests
    {
        // L1-REG-06 | State-Invalid | Không cho huỷ đơn đã sang trạng thái không được huỷ
        //
        // 🔴 SPEC GAP v2.2 (nhánh InDelivery): RequestCancelOrderAsync chỉ chặn theo OrderStatus
        //   (chỉ cho PendingPayment / PendingConfirmation / Confirmed / Processing). Đơn ĐANG TRÊN ĐƯỜNG GIAO
        //   có OrderStatus = Processing + DeliveryStatus = InDelivery -> vẫn huỷ được.
        //   SRS 4.4.1 và FT-08 NAC-05 yêu cầu chặn cả 'In Delivery'. Test ĐỎ ở nhánh InDelivery
        //   cho tới khi guard xét thêm DeliveryStatus.
        [Theory]
        [InlineData(OrderStatus.Processing, DeliveryStatus.InDelivery)]  // đang giao
        [InlineData(OrderStatus.Completed, DeliveryStatus.Delivered)]    // đã giao xong
        public async Task L1_REG_06_RequestCancel_OnNonCancellableOrder_IsConflict(
            OrderStatus orderStatus, DeliveryStatus deliveryStatus)
        {
            var order = SeedOrder(o =>
            {
                o.OrderStatus = orderStatus;
                o.DeliveryStatus = deliveryStatus;
            });

            var act = () => _sut.RequestCancelOrderAsync(order.Id, _customer.Id,
                new RequestCancelOrderDto { Reason = "Khách đổi ý" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ChangeTracker.Clear();
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus
                .Should().NotBe(OrderStatus.CancelRequested, "không được tạo yêu cầu huỷ");
        }

        // L1-REG-07 | State-Invalid | Không cho thanh toán lại đơn ĐÃ Paid -> không sinh QR/giao dịch thứ 2
        //
        // 🔴 SPEC GAP v2.2: GenerateSePayQrAsync chỉ kiểm tra quyền truy cập, KHÔNG kiểm tra PaymentStatus.
        //   Đơn đã Paid vẫn sinh được QR mới -> khách có thể chuyển khoản lần 2.
        //   Test ĐỎ cho tới khi bổ sung state guard (FT-03 NAC-02; BR-016).
        [Fact]
        public async Task L1_REG_07_GenerateSePayQr_OnPaidOrder_IsConflict()
        {
            var order = SeedOrder(o =>
            {
                o.PaymentMethod = PaymentMethod.SePay;
                o.PaymentStatus = PaymentStatus.Paid;
                o.OrderStatus = OrderStatus.Confirmed;
            });

            var act = () => _sut.GenerateSePayQrAsync(order.Id, _customer.Id, "Customer");

            await act.Should().ThrowAsync<InvalidOperationException>("đơn đã thanh toán không được sinh QR mới");
        }
    }
}
