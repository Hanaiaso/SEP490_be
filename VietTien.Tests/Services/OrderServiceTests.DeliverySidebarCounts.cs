using FluentAssertions;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// GetSalesDeliverySidebarCountsAsync — số badge sidebar mục "Giao hàng" của Sales. Phải khớp
    /// đúng điều kiện lọc mà SalesDeliveryPage.tsx đang tự tính phía client (xem comment trong
    /// OrderService.cs), tái dùng GetDeliveryOrdersAsync/GetPendingPickupsAsync làm nguồn duy nhất.
    /// </summary>
    public partial class OrderServiceTests
    {
        // DEL-SB-01 | EP-Valid | Đơn Consolidated của đúng Sales Staff -> tính vào PendingHandover
        [Fact]
        public async Task DEL_SB_01_PendingHandover_CountsOnlyConsolidatedOrdersForThisSalesStaff()
        {
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Consolidated; });
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Ready; }); // chưa tập kết -> không tính

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.PendingHandover.Should().Be(1);
        }

        // DEL-SB-02 | EP-Valid | Đơn chưa đạt trạng thái "đã xử lý xong ở kho" -> tính vào WarehouseCoordPending
        [Fact]
        public async Task DEL_SB_02_WarehouseCoordPending_ExcludesOrdersAlreadyReadyOrBeyond()
        {
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Picking; });   // đang xử lý -> tính
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Fulfilled; }); // đã xong -> không tính

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.WarehouseCoordPending.Should().Be(1);
        }

        // DEL-SB-03 | EP-Valid | Đơn đã sẵn sàng giao, chưa xếp xe -> tính vào TripsPending (không phải Transfer)
        [Fact]
        public async Task DEL_SB_03_TripsPending_CountsUnscheduledFulfilledOrders()
        {
            SeedOrder(o =>
            {
                o.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                o.DeliveryStatus = DeliveryStatus.NotScheduled;
                o.PaymentMethod = PaymentMethod.COD;
            });

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.TripsPending.Should().Be(1);
        }

        // DEL-SB-04 | EP-Valid | Đơn đã xếp xe (Scheduled) -> không còn tính vào TripsPending mà tính vào CollectionPending
        [Fact]
        public async Task DEL_SB_04_ScheduledOrder_MovesFromTripsPendingToCollectionPending()
        {
            SeedOrder(o =>
            {
                o.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                o.DeliveryStatus = DeliveryStatus.Scheduled;
                o.PaymentMethod = PaymentMethod.COD;
            });

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.TripsPending.Should().Be(0, "đơn đã được xếp xe không còn là việc 'chờ xếp chuyến' nữa");
            result.CollectionPending.Should().Be(1, "đơn đã lên lịch giao -> chuyển sang chờ giao/thu tiền");
        }

        // DEL-SB-05 | EP-Valid | Đơn khác Sales Staff không được tính vào bất kỳ số nào của mình
        [Fact]
        public async Task DEL_SB_05_OrdersOfOtherSalesStaff_AreNotCounted()
        {
            var otherStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherStaff);
            var (_, otherProfile) = TestData.SeedCustomer(_db, u => u.IsPhoneVerified = true);
            otherProfile.AssignedSalesStaffId = otherStaff.Id;
            _db.SaveChanges();
            var otherOrder = TestData.Order(otherProfile.Id, o => o.FulfillmentStatus = FulfillmentStatus.Consolidated);
            _db.Orders.Add(otherOrder);
            _db.SaveChanges();

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.PendingHandover.Should().Be(0);
            result.WarehouseCoordPending.Should().Be(0);
        }
    }
}
