using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — ⊕ v2.2 block Ngưỡng giao hỏng (L1-ORD-72..73).
    /// Dùng chung fixture với OrderServiceTests (partial class).
    /// </summary>
    public partial class OrderServiceTests
    {
        private Order SeedDeliverableOrder(int failedCount)
            => SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Confirmed;
                o.DeliveryStatus = DeliveryStatus.InDelivery;
                o.FailedDeliveryCount = failedCount;
                o.IsBlockedForDelivery = false;
                o.ScheduledDeliveryDate = null;
                o.FinalPayment = 1_000_000m;
            });

        private static RecordDeliveryResultDto FailedDelivery() => new()
        {
            DeliveryOutcome = "failed",
            AmountCollected = 0,
            Notes = "Khách không nghe máy, không có người nhận."
        };

        // L1-ORD-72 | BVA | FailedDeliveryCount quanh ngưỡng 3: dưới ngưỡng vẫn cho giao lại,
        // từ lần thứ 3 trở đi thì khoá đơn và escalate cho Sales Manager.
        [Theory]
        [InlineData(1, false)] // 1 -> 2 lần hỏng, chưa chạm ngưỡng
        [InlineData(2, true)]  // 2 -> 3 lần hỏng, chạm ngưỡng -> khoá
        public async Task L1_ORD_72_DeliveryFailureThreshold(int failedCountBefore, bool expectBlocked)
        {
            var order = SeedDeliverableOrder(failedCountBefore);

            await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, FailedDelivery());

            _db.ChangeTracker.Clear();
            var saved = _db.Orders.Single(o => o.Id == order.Id);
            saved.FailedDeliveryCount.Should().Be(failedCountBefore + 1);
            saved.IsBlockedForDelivery.Should().Be(expectBlocked);
            saved.DeliveryStatus.Should().Be(expectBlocked ? DeliveryStatus.Failed : DeliveryStatus.Rescheduled);
        }

        // L1-ORD-73 | EP-Invalid | Ngưỡng giao hỏng phải ĐỌC TỪ SystemConfig, không hard-code
        //
        // 🔴 DEFECT CANDIDATE v2.2 (doc đã đánh dấu):
        //   OrderService.cs:2077 viết cứng `if (order.FailedDeliveryCount >= 3)`, hoàn toàn bỏ qua
        //   SystemConfig key 'DELIVERY_FAILURE_MANAGER_THRESHOLD' (đã seed sẵn "3" trong
        //   ApplicationDbContext.cs:1049). Khi Admin đổi ngưỡng thành 5, đơn có 3 lần hỏng VẪN bị khoá.
        //   Test này assert THEO SPEC nên sẽ ĐỎ cho tới khi code đọc ngưỡng từ cấu hình.
        [Fact]
        public async Task L1_ORD_73_DeliveryFailureThreshold_IsReadFromSystemConfig()
        {
            // Admin nâng ngưỡng từ 3 lên 5 qua System Config.
            _sysConfig.Setup(s => s.GetEffectiveValueAsync("DELIVERY_FAILURE_MANAGER_THRESHOLD", It.IsAny<DateTime?>()))
                      .ReturnsAsync("5");

            var order = SeedDeliverableOrder(failedCount: 2); // lần hỏng tiếp theo là lần thứ 3

            await _sut.RecordDeliveryResultAsync(order.Id, _salesStaff.Id, FailedDelivery());

            _db.ChangeTracker.Clear();
            var saved = _db.Orders.Single(o => o.Id == order.Id);
            saved.FailedDeliveryCount.Should().Be(3);
            saved.IsBlockedForDelivery.Should().BeFalse(
                "ngưỡng đã được Admin đổi thành 5 nên 3 lần hỏng chưa được phép khoá đơn");
        }
    }
}
