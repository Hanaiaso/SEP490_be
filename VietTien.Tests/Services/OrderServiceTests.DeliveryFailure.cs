using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
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

        // ─── P2-6: Sales Manager mở khóa đơn & tất toán công nợ (UC-35) ───────

        private Order SeedBlockedOrder()
            => SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Confirmed;
                o.DeliveryStatus = DeliveryStatus.Failed;
                o.FailedDeliveryCount = 3;
                o.IsBlockedForDelivery = true;
            });

        private CustomerDebt SeedDebt(Order order, DebtStatus status = DebtStatus.InDebt, decimal amount = 500_000m, int daysAgo = 0)
        {
            var debt = new CustomerDebt
            {
                CustomerProfileId = order.CustomerProfileId,
                OrderId = order.Id,
                DebtAmount = amount,
                Status = status,
                CreatedAt = DateTime.UtcNow.AddDays(-daysAgo)
            };
            _db.CustomerDebts.Add(debt);
            _db.SaveChanges();
            return debt;
        }

        [Fact]
        public async Task P2_6_UnblockOrder_WhenBlocked_ResetsFlagsAndCountAndRecordsAudit()
        {
            var order = SeedBlockedOrder();
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();

            await _sut.UnblockOrderForRedeliveryAsync(order.Id, manager.Id, "Khách đã xác nhận lịch giao lại qua điện thoại.");

            _db.ChangeTracker.Clear();
            var saved = _db.Orders.Single(o => o.Id == order.Id);
            saved.IsBlockedForDelivery.Should().BeFalse();
            saved.FailedDeliveryCount.Should().Be(0, "phải reset để lần giao thất bại tiếp theo không bị khoá lại ngay");
            saved.DeliveryStatus.Should().Be(DeliveryStatus.Rescheduled);
            saved.UnblockedByUserId.Should().Be(manager.Id);
            saved.UnblockedAt.Should().NotBeNull();
            saved.UnblockReason.Should().Be("Khách đã xác nhận lịch giao lại qua điện thoại.");
        }

        [Fact]
        public async Task P2_6_UnblockOrder_WhenNotBlocked_Rejected()
        {
            var order = SeedOrder(o => o.IsBlockedForDelivery = false);
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();

            var act = () => _sut.UnblockOrderForRedeliveryAsync(order.Id, manager.Id, "ly do");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task P2_6_SettleDebt_WhenInDebt_MarksSettledAndZeroesAmount()
        {
            var order = SeedOrder();
            var debt = SeedDebt(order);
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();

            await _sut.SettleDebtAsync(debt.Id, manager.Id, "Khách chuyển khoản bổ sung.");

            _db.ChangeTracker.Clear();
            var saved = _db.CustomerDebts.Single(d => d.Id == debt.Id);
            saved.Status.Should().Be(DebtStatus.Settled);
            saved.DebtAmount.Should().Be(0);
            saved.SettledByUserId.Should().Be(manager.Id);
            saved.SettledAt.Should().NotBeNull();
            saved.SettlementNote.Should().Be("Khách chuyển khoản bổ sung.");
        }

        [Fact]
        public async Task P2_6_SettleDebt_AlreadySettled_Rejected()
        {
            var order = SeedOrder();
            var debt = SeedDebt(order, status: DebtStatus.Settled, amount: 0);
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();

            var act = () => _sut.SettleDebtAsync(debt.Id, manager.Id, null);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task P2_6_GetDebts_ComputesOverdueDaysFromCreatedAt()
        {
            var order = SeedOrder();
            SeedDebt(order, daysAgo: 10);

            var result = await _sut.GetDebtsAsync();

            result.Should().ContainSingle();
            result[0].OverdueDays.Should().BeInRange(9, 10, "seed lùi 10 ngày, cho phép sai số làm tròn/độ trễ chạy test");
        }

        [Fact]
        public async Task P2_6_GetBlockedOrders_OnlyReturnsBlockedOrders()
        {
            var blocked = SeedBlockedOrder();
            SeedOrder(o => o.IsBlockedForDelivery = false);

            var result = await _sut.GetBlockedOrdersAsync();

            result.Should().ContainSingle(o => o.OrderId == blocked.Id);
        }
    }
}
