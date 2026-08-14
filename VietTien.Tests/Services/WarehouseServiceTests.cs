using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: WarehouseService — L1-WH-01..12. EF InMemory + mock IHubContext&lt;SalesHub&gt; + INotificationService.
    /// Ghi chú map trạng thái thực tế: 'Packing' trong spec = OrderStatus.Processing + FulfillmentStatus.Picking;
    /// tabType hợp lệ: OnlinePending/ExternalPending/InProgress/Consolidation/Handover/GoodsIssue.
    /// Lệch spec L1-WH-06: ReportShortageAsync guard theo QUYỀN SỞ HỮU ticket (WarehouseStaffId), không guard theo trạng thái đơn.
    /// </summary>
    public class WarehouseServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _noti = new();
        private readonly WarehouseService _sut;
        private readonly CustomerProfile _profile;
        private readonly User _whStaff;
        private readonly Product _p1;

        public WarehouseServiceTests()
        {
            _sut = new WarehouseService(_db, MockHubContext.Create<SalesHub>().Object, _noti.Object, new Mock<ILogger<WarehouseService>>().Object);
            (_, _profile) = TestData.SeedCustomer(_db);
            _whStaff = TestData.User(u => u.Role = SystemRole.WarehouseStaff);
            _db.Users.Add(_whStaff);
            _p1 = TestData.SeedProduct(_db);
        }

        private Order SeedOrder(OrderStatus status, FulfillmentStatus fulfillment = FulfillmentStatus.Unallocated,
            Guid? whStaffId = null, int qty = 4, DateTime? createdAt = null)
        {
            var order = TestData.Order(_profile.Id, o =>
            {
                o.OrderStatus = status;
                o.FulfillmentStatus = fulfillment;
                o.WarehouseStaffId = whStaffId;
                if (createdAt.HasValue) o.CreatedAt = createdAt.Value;
            });
            _db.Orders.Add(order);
            _db.OrderItems.Add(TestData.OrderItem(order.Id, _p1.Id, qty));
            _db.SaveChanges();
            return order;
        }

        //  ▶ Block: GetOrdersForWarehouseAsync()

        // L1-WH-01 | EP-Valid | FIFO: đơn confirm 08:00 đứng trước đơn confirm 08:05
        [Fact]
        public async Task L1_WH_01_GetOrders_FifoOrdering()
        {
            var t0 = DateTime.UtcNow.AddHours(-2);
            var orderA = SeedOrder(OrderStatus.Confirmed, createdAt: t0);
            var orderB = SeedOrder(OrderStatus.Confirmed, createdAt: t0.AddMinutes(5));

            var result = await _sut.GetOrdersForWarehouseAsync("OnlinePending", 1, 20);

            result.Should().HaveCount(2);
            result[0].OrderId.Should().Be(orderA.Id); // đơn cũ hơn đứng trước
            result[1].OrderId.Should().Be(orderB.Id);
        }

        // L1-WH-02 | EP-Valid | tabType lọc đúng trạng thái: 'InProgress' chỉ trả đơn đang Picking
        [Fact]
        public async Task L1_WH_02_GetOrders_TabTypeFiltersStatus()
        {
            SeedOrder(OrderStatus.Confirmed, FulfillmentStatus.Unallocated);
            var picking = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Picking);
            SeedOrder(OrderStatus.Processing, FulfillmentStatus.Consolidated);

            var result = await _sut.GetOrdersForWarehouseAsync("InProgress", 1, 20);

            result.Should().ContainSingle().Which.OrderId.Should().Be(picking.Id);
        }

        //  ▶ Block: AcceptOrderAsync()

        // L1-WH-03 | State-Valid | Đơn đã phân bổ (Allocated) được kho tiếp nhận -> Picking, ticket khóa theo staff, sinh PickTask
        // (Code mới: guard theo FulfillmentStatus thay vì OrderStatus; cần kho WH-DEFAULT để sinh PickTask.)
        [Fact]
        public async Task L1_WH_03_AcceptOrder_Confirmed_LockedToStaff()
        {
            SeedDefaultWarehouseStock(_p1.Id, 10);
            var order = SeedOrder(OrderStatus.Confirmed, FulfillmentStatus.Allocated);

            await _sut.AcceptOrderAsync(order.Id, _whStaff.Id);

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.FulfillmentStatus.Should().Be(FulfillmentStatus.Picking);
            updated.WarehouseStaffId.Should().Be(_whStaff.Id);
            updated.PickingStartedAt.Should().NotBeNull();
            _db.PickTasks.Should().Contain(pt => pt.OrderId == order.Id); // ticket soạn hàng được tạo
        }

        // L1-WH-04 | State-Invalid | Tiếp nhận đơn ở trạng thái không hợp lệ (đã tập kết) -> conflict, không khóa ticket
        [Fact]
        public async Task L1_WH_04_AcceptOrder_NotConfirmed_Rejected()
        {
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Consolidated);

            var act = () => _sut.AcceptOrderAsync(order.Id, _whStaff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Đơn hàng chưa được phân bổ hoặc không ở trạng thái hợp lệ, không thể xử lý.");
            _db.Orders.Single(o => o.Id == order.Id).WarehouseStaffId.Should().BeNull();
        }

        //  ▶ Block: ReportShortageAsync()

        // L1-WH-05 | State-Valid | Đang picking, báo thiếu hàng -> đơn quay về PendingConfirmation, SalesManager được báo
        [Fact]
        public async Task L1_WH_05_ReportShortage_HaltsPackingAndNotifiesSales()
        {
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Picking, _whStaff.Id);

            await _sut.ReportShortageAsync(order.Id, _whStaff.Id, new ShortageAlertRequestDto
            {
                ProductId = _p1.Id,
                MissingQuantity = 2,
                Note = "Thiếu hàng khu A"
            });

            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.OrderStatus.Should().Be(OrderStatus.PendingConfirmation);
            updated.FulfillmentStatus.Should().Be(FulfillmentStatus.Unallocated);
            _noti.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_07_WarehouseShortage, SystemRole.SalesManager,
                It.IsAny<string>(), It.IsAny<string>(), order.Id, "Order"), Times.Once);
        }

        // L1-WH-06 | EP-Invalid | Staff không sở hữu ticket báo thiếu -> từ chối, trạng thái không đổi
        // (Lệch spec: guard thực tế theo WarehouseStaffId, không theo trạng thái đơn.)
        [Fact]
        public async Task L1_WH_06_ReportShortage_NotTicketOwner_Rejected()
        {
            var otherStaff = Guid.NewGuid();
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Picking, otherStaff);

            var act = () => _sut.ReportShortageAsync(order.Id, _whStaff.Id, new ShortageAlertRequestDto
            {
                ProductId = _p1.Id,
                MissingQuantity = 1
            });

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Bạn không có quyền báo cáo cho đơn hàng này.");
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Processing);
        }

        //  ▶ Block: CompletePickTaskAsync() / ConsolidateOrderAsync()

        /// <summary>Code mới: CompletePickTaskAsync thao tác trên PickTask (không phải Order) — seed 1 task.</summary>
        private PickTask SeedPickTask(Guid orderId, PickTaskStatus status, Guid? assignedTo, bool fullyPicked = true)
        {
            var (w, _) = TestData.Warehouse();
            _db.Warehouses.Add(w);
            var task = new PickTask
            {
                OrderId = orderId,
                WarehouseId = w.Id,
                Status = status,
                AssignedUserId = assignedTo,
            };
            task.Items.Add(new PickTaskItem
            {
                PickTaskId = task.Id,
                ProductId = _p1.Id,
                QuantityToPick = 4,
                PickedQuantity = fullyPicked ? 4 : 2,
            });
            _db.PickTasks.Add(task);
            _db.SaveChanges();
            return task;
        }

        // L1-WH-07 | State-Valid | Hoàn tất pick task (đã gom đủ hàng) -> task Completed; mọi task xong -> đơn Ready
        [Fact]
        public async Task L1_WH_07_CompletePickTask_PickingDone_MovesToReady()
        {
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Picking, _whStaff.Id);
            var task = SeedPickTask(order.Id, PickTaskStatus.Picking, _whStaff.Id, fullyPicked: true);

            await _sut.CompletePickTaskAsync(task.Id, _whStaff.Id);

            _db.PickTasks.Single(pt => pt.Id == task.Id).Status.Should().Be(PickTaskStatus.Completed);
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.FulfillmentStatus.Should().Be(FulfillmentStatus.Ready);
            updated.PickingCompletedAt.Should().NotBeNull();
        }

        // L1-WH-08 | EP-Invalid | Staff không sở hữu lệnh (task khóa cho người khác) -> từ chối, trạng thái không đổi
        [Fact]
        public async Task L1_WH_08_CompletePickTask_NotOwner_Rejected()
        {
            var owner = Guid.NewGuid();
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Picking, owner);
            var task = SeedPickTask(order.Id, PickTaskStatus.Picking, assignedTo: owner);

            var act = () => _sut.CompletePickTaskAsync(task.Id, _whStaff.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Bạn không có quyền thao tác trên lệnh này.");
            _db.PickTasks.Single(pt => pt.Id == task.Id).Status.Should().Be(PickTaskStatus.Picking);
        }

        // L1-WH-09 | State-Valid | Tập kết sau khi mọi pick task xong (Ready) -> Consolidated
        [Fact]
        public async Task L1_WH_09_Consolidate_AfterReady_Consolidated()
        {
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Ready, _whStaff.Id);

            await _sut.ConsolidateOrderAsync(order.Id, _whStaff.Id);

            _db.Orders.Single(o => o.Id == order.Id).FulfillmentStatus.Should().Be(FulfillmentStatus.Consolidated);
        }

        //  ▶ Block: HandoverOrderAsync() / PostGoodsIssueAsync()

        // L1-WH-10 | EP-Valid | Bàn giao kèm chữ ký -> HandoverRecord lưu chữ ký + staff + thời điểm, đơn HandedOver
        // (Code mới: cần ĐỦ 2 chữ ký Kho + Sales thì handover mới Confirmed và đơn mới sang HandedOver.)
        [Fact]
        public async Task L1_WH_10_Handover_WithSignature_RecordCreated()
        {
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.Consolidated, _whStaff.Id);

            // BR-034: callerRole=Admin vì test này ký cả 2 phía trong 1 lần gọi (kiểm tra logic tạo
            // HandoverRecord đầy đủ) — WarehouseStaff/SalesStaff bị chặn ký thay phía kia, chỉ CEO/Admin
            // được phép ký cả hai.
            await _sut.HandoverOrderAsync(order.Id, _whStaff.Id, nameof(SystemRole.Admin), new HandoverRequestDto
            {
                WarehouseSignature = "wh-signature",
                SalesSignature = "sales-signature"
            });

            _db.Orders.Single(o => o.Id == order.Id).FulfillmentStatus.Should().Be(FulfillmentStatus.HandedOver);
            var record = _db.HandoverRecords.Single(h => h.OrderId == order.Id);
            record.WarehouseStaffId.Should().Be(_whStaff.Id);
            record.WarehouseSignature.Should().Be("wh-signature");
            record.Status.Should().Be(HandoverStatus.Confirmed);
            record.HandoverTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        }

        /// <summary>Code mới yêu cầu kho tập kết có Code = "WH-DEFAULT" — seed tồn kho tại đó.</summary>
        private void SeedDefaultWarehouseStock(Guid productId, int onHand)
        {
            var (w, loc) = TestData.Warehouse(x => x.Code = "WH-DEFAULT");
            _db.Warehouses.Add(w);
            _db.Inventories.Add(TestData.Inventory(productId, loc.Id, onHand));
            _db.SaveChanges();
        }

        // L1-WH-11 | State-Valid | Xuất kho sau bàn giao -> tồn kho trừ đúng số lượng, đơn Fulfilled, GoodsIssue Posted
        [Fact]
        public async Task L1_WH_11_PostGoodsIssue_StockDecremented()
        {
            SeedDefaultWarehouseStock(_p1.Id, 10);
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.HandedOver, _whStaff.Id, qty: 4);

            await _sut.PostGoodsIssueAsync(order.Id, _whStaff.Id);

            _db.Inventories.Single(i => i.ProductId == _p1.Id).OnHandQuantity.Should().Be(6);
            _db.Orders.Single(o => o.Id == order.Id).FulfillmentStatus.Should().Be(FulfillmentStatus.Fulfilled);
            _db.GoodsIssues.Should().ContainSingle(gi => gi.ReferenceId == order.Id && gi.Status == GoodsIssueStatus.Posted);
        }

        // L1-WH-12 | EP-Invalid | Xuất kho khi 1 SKU thiếu tồn -> chặn toàn bộ, KHÔNG trừ một phần (all-or-nothing)
        [Fact]
        public async Task L1_WH_12_PostGoodsIssue_InsufficientStock_NoPartialDecrement()
        {
            var p2 = TestData.SeedProduct(_db);
            SeedDefaultWarehouseStock(_p1.Id, 10);   // P1 đủ (tại WH-DEFAULT)
            var defaultLoc = _db.WarehouseLocations.Single(l => l.Warehouse.Code == "WH-DEFAULT");
            _db.Inventories.Add(TestData.Inventory(p2.Id, defaultLoc.Id, 1)); // P2 thiếu (cần 2)
            _db.SaveChanges();
            var order = SeedOrder(OrderStatus.Processing, FulfillmentStatus.HandedOver, _whStaff.Id, qty: 4);
            _db.OrderItems.Add(TestData.OrderItem(order.Id, p2.Id, 2));
            _db.SaveChanges();

            var act = () => _sut.PostGoodsIssueAsync(order.Id, _whStaff.Id);

            await act.Should().ThrowAsync<Exception>().WithMessage("Không đủ tồn kho khả dụng*");
            // Xả change tracker để đọc lại trạng thái ĐÃ LƯU: P1 không được trừ một phần
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.ProductId == _p1.Id).OnHandQuantity.Should().Be(10);
            _db.Orders.Single(o => o.Id == order.Id).FulfillmentStatus.Should().Be(FulfillmentStatus.HandedOver);
        }
    }
}
