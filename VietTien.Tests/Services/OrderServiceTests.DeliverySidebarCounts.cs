using FluentAssertions;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// GetSalesDeliverySidebarCountsAsync — số badge sidebar mục "Giao hàng" của Sales. Phải khớp
    /// đúng điều kiện lọc + đúng field scoping mà từng trang thật đang dùng (xem comment trong
    /// OrderService.cs) — TripsPending/ArrangementPending/CollectionPending tái dùng nguyên
    /// GetDeliveryOrdersAsync/GetPendingPickupsAsync; WarehouseCoordPending lọc theo Order.SalesStaffId
    /// (snapshot, giống GetSalesOrdersAsync — KHÔNG phải CustomerProfile.AssignedSalesStaffId hiện tại,
    /// phát hiện lệch số 25 vs 26 khi smoke test); PendingHandover là hàng đợi CHUNG không lọc theo staff.
    /// </summary>
    public partial class OrderServiceTests
    {
        // DEL-SB-01 | EP-Valid | PendingHandover là hàng đợi CHUNG toàn hệ thống, không lọc theo Sales Staff gọi
        [Fact]
        public async Task DEL_SB_01_PendingHandover_IsGlobalNotScopedByCaller()
        {
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Consolidated; });
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Ready; }); // chưa tập kết -> không tính

            var otherStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherStaff);
            _db.SaveChanges();
            var otherOrder = TestData.Order(_profile.Id, o => { o.FulfillmentStatus = FulfillmentStatus.Consolidated; o.SalesStaffId = otherStaff.Id; });
            _db.Orders.Add(otherOrder);
            _db.SaveChanges();

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.PendingHandover.Should().Be(2, "Bàn giao Sales là hàng đợi chung — bất kỳ Sales nào đăng nhập cũng thấy đủ mọi đơn đang chờ ký, không riêng đơn của mình");
        }

        // DEL-SB-02 | EP-Valid | WarehouseCoordPending lọc theo Order.SalesStaffId (snapshot), khớp GetSalesOrdersAsync
        [Fact]
        public async Task DEL_SB_02_WarehouseCoordPending_ScopesBySalesStaffIdSnapshot()
        {
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Picking; o.SalesStaffId = _salesStaff.Id; });   // đang xử lý, của mình -> tính
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Fulfilled; o.SalesStaffId = _salesStaff.Id; }); // đã xong -> không tính

            var otherStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherStaff);
            _db.SaveChanges();
            // Đơn của Sales KHÁC (dù cùng khách hàng do _salesStaff phụ trách hiện tại) -> không được tính,
            // vì đây là snapshot Sale lúc TẠO đơn, không phải chủ khách hiện tại (đúng ý nghĩa "đổi Sale").
            SeedOrder(o => { o.FulfillmentStatus = FulfillmentStatus.Picking; o.SalesStaffId = otherStaff.Id; });

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

        // DEL-SB-05 | EP-Valid | Đơn của khách hàng do Sales khác phụ trách (AssignedSalesStaffId khác)
        // không được tính vào TripsPending/ArrangementPending/CollectionPending của mình
        [Fact]
        public async Task DEL_SB_05_OrdersOfOtherAssignedSalesStaff_AreNotCountedInDeliveryOrderBasedMetrics()
        {
            var otherStaff = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherStaff);
            var (_, otherProfile) = TestData.SeedCustomer(_db, u => u.IsPhoneVerified = true);
            otherProfile.AssignedSalesStaffId = otherStaff.Id;
            _db.SaveChanges();
            var otherOrder = TestData.Order(otherProfile.Id, o =>
            {
                o.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                o.DeliveryStatus = DeliveryStatus.NotScheduled;
                o.PaymentMethod = PaymentMethod.COD;
            });
            _db.Orders.Add(otherOrder);
            _db.SaveChanges();

            var result = await _sut.GetSalesDeliverySidebarCountsAsync(_salesStaff.Id);

            result.TripsPending.Should().Be(0);
        }
    }
}
