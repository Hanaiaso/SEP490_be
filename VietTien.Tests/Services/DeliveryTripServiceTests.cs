using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Delivery;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: DeliveryTripService — luồng chuyến xe (Nhóm C) mới: tải trọng xe, trạng thái Loading,
    /// thêm/rút đơn khi đang bốc hàng, điều kiện xuất phát (HandoverRecord Confirmed).
    /// EF InMemory + mock INotificationService.
    /// </summary>
    public class DeliveryTripServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<INotificationService> _noti = new();
        private readonly DeliveryTripService _sut;
        private readonly CustomerProfile _profile;
        private readonly Product _p1;

        public DeliveryTripServiceTests()
        {
            _sut = new DeliveryTripService(_db, _noti.Object);
            (_, _profile) = TestData.SeedCustomer(_db);
            _p1 = TestData.SeedProduct(_db);
        }

        private Vehicle SeedVehicle(decimal? capacityKg)
        {
            var v = TestData.Vehicle(Random.Shared.Next(1, 999_999), x => x.Capacity = capacityKg);
            _db.Vehicles.Add(v);
            _db.SaveChanges();
            return v;
        }

        private Order SeedPackedOrder(decimal? weightKg, DeliveryStatus deliveryStatus = DeliveryStatus.NotScheduled)
        {
            var order = TestData.Order(_profile.Id, o =>
            {
                o.OrderStatus = OrderStatus.Processing;
                o.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                o.TotalPackedWeightKg = weightKg;
                o.DeliveryStatus = deliveryStatus;
            });
            _db.Orders.Add(order);
            _db.OrderItems.Add(TestData.OrderItem(order.Id, _p1.Id, 1));
            _db.SaveChanges();
            return order;
        }

        private void SeedConfirmedHandover(Guid orderId)
        {
            _db.HandoverRecords.Add(new HandoverRecord
            {
                OrderId = orderId,
                Status = HandoverStatus.Confirmed,
                WarehouseSignature = "wh-sig",
                SalesSignature = "sales-sig",
                HandoverTime = DateTime.UtcNow,
            });
            _db.SaveChanges();
        }

        // DEL-01 | EP-Valid | Tạo chuyến với đơn trong hạn tải trọng xe -> Scheduled, gán đúng đơn
        [Fact]
        public async Task DEL_01_CreateTrip_WithinCapacity_Succeeds()
        {
            var vehicle = SeedVehicle(1000m);
            var order = SeedPackedOrder(300m);

            var result = await _sut.CreateTripAsync(Guid.NewGuid(), new CreateDeliveryTripRequestDto
            {
                VehicleId = vehicle.Id,
                Shift = "Sáng",
                TripDate = DateTime.UtcNow.Date.AddDays(1),
                OrderIds = new List<Guid> { order.Id }
            });

            result.Status.Should().Be(nameof(DeliveryTripStatus.Scheduled));
            result.OrderIds.Should().ContainSingle().Which.Should().Be(order.Id);
            result.TotalWeightKg.Should().Be(300m);
            result.RemainingCapacityKg.Should().Be(700m);
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.DeliveryTripId.Should().Be(result.Id);
            // BUGFIX: OrderDetail.jsx/SalesOrderDetailPage.tsx vẫn đọc 3 field cũ này để hiện
            // ngày/ca giao dự kiến — luồng Trip-based (mới) phải đồng bộ ngược, không để trống.
            updated.DeliveryVehicleId.Should().Be(vehicle.VehicleNumber);
            updated.DeliveryShift.Should().Be("Sáng");
            updated.ScheduledDeliveryDate.Should().Be(DateTime.UtcNow.Date.AddDays(1));
        }

        // DEL-02 | EP-Invalid | Tổng trọng lượng đơn vượt tải trọng xe -> chặn cứng, không tạo chuyến
        [Fact]
        public async Task DEL_02_CreateTrip_OverCapacity_Rejected()
        {
            var vehicle = SeedVehicle(500m);
            var order = SeedPackedOrder(800m);

            var act = () => _sut.CreateTripAsync(Guid.NewGuid(), new CreateDeliveryTripRequestDto
            {
                VehicleId = vehicle.Id,
                Shift = "Sáng",
                TripDate = DateTime.UtcNow.Date.AddDays(1),
                OrderIds = new List<Guid> { order.Id }
            });

            await act.Should().ThrowAsync<VehicleOverweightException>();
            _db.DeliveryTrips.Should().BeEmpty();
            _db.Orders.Single(o => o.Id == order.Id).DeliveryTripId.Should().BeNull();
        }

        // DEL-03 | State-Valid | Bắt đầu bốc hàng -> Scheduled chuyển sang Loading, lưu giờ dự kiến
        [Fact]
        public async Task DEL_03_StartLoading_FromScheduled_MovesToLoading()
        {
            var vehicle = SeedVehicle(1000m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Scheduled };
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();

            var departure = DateTime.UtcNow.AddHours(1);
            var arrival = DateTime.UtcNow.AddHours(4);
            var result = await _sut.StartLoadingAsync(trip.Id, new StartLoadingRequestDto { PlannedDepartureAt = departure, PlannedArrivalAt = arrival });

            result.Status.Should().Be(nameof(DeliveryTripStatus.Loading));
            result.PlannedDepartureAt.Should().Be(departure);
            result.PlannedArrivalAt.Should().Be(arrival);
        }

        // DEL-04 | EP-Invalid | Thêm đơn khi chuyến chưa ở Loading -> từ chối
        [Fact]
        public async Task DEL_04_AddOrders_TripNotLoading_Rejected()
        {
            var vehicle = SeedVehicle(1000m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Scheduled };
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();
            var order = SeedPackedOrder(100m);

            var act = () => _sut.AddOrdersToTripAsync(trip.Id, new AddOrdersToTripRequestDto { OrderIds = new List<Guid> { order.Id } });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // DEL-05 | EP-Invalid | Chuyến đang Loading đã gần đầy -> thêm đơn mới khiến vượt tải -> chặn cứng
        [Fact]
        public async Task DEL_05_AddOrders_WouldExceedCapacity_Rejected()
        {
            var vehicle = SeedVehicle(500m);
            var existingOrder = SeedPackedOrder(400m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Loading };
            trip.Orders.Add(existingOrder);
            existingOrder.DeliveryTripId = trip.Id;
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();
            var newOrder = SeedPackedOrder(200m);

            var act = () => _sut.AddOrdersToTripAsync(trip.Id, new AddOrdersToTripRequestDto { OrderIds = new List<Guid> { newOrder.Id } });

            await act.Should().ThrowAsync<VehicleOverweightException>();
            _db.Orders.Single(o => o.Id == newOrder.Id).DeliveryTripId.Should().BeNull();
        }

        // DEL-05b | State-Valid | Thêm đơn trong hạn tải trọng -> gán đúng đơn + đồng bộ ngược 3 field cũ
        [Fact]
        public async Task DEL_05b_AddOrders_WithinCapacity_SyncsLegacyDeliveryFields()
        {
            var vehicle = SeedVehicle(1000m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Trưa", TripDate = DateTime.UtcNow.Date.AddDays(2), Status = DeliveryTripStatus.Loading };
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();
            var order = SeedPackedOrder(200m);

            var result = await _sut.AddOrdersToTripAsync(trip.Id, new AddOrdersToTripRequestDto { OrderIds = new List<Guid> { order.Id } });

            result.OrderIds.Should().ContainSingle().Which.Should().Be(order.Id);
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.DeliveryTripId.Should().Be(trip.Id);
            updated.DeliveryVehicleId.Should().Be(vehicle.VehicleNumber);
            updated.DeliveryShift.Should().Be("Trưa");
            updated.ScheduledDeliveryDate.Should().Be(trip.TripDate);
        }

        // DEL-06 | State-Valid | Rút đơn khỏi chuyến đang Loading -> đơn trở lại NotScheduled, không còn gắn chuyến
        [Fact]
        public async Task DEL_06_RemoveOrder_FromLoadingTrip_Unassigned()
        {
            var vehicle = SeedVehicle(1000m);
            var order = SeedPackedOrder(300m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Loading };
            trip.Orders.Add(order);
            order.DeliveryTripId = trip.Id;
            order.DeliveryStatus = DeliveryStatus.Scheduled;
            order.DeliveryVehicleId = vehicle.VehicleNumber;
            order.DeliveryShift = "Sáng";
            order.ScheduledDeliveryDate = trip.TripDate;
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();

            var result = await _sut.RemoveOrderFromTripAsync(trip.Id, order.Id);

            result.OrderIds.Should().BeEmpty();
            var updated = _db.Orders.Single(o => o.Id == order.Id);
            updated.DeliveryTripId.Should().BeNull();
            updated.DeliveryStatus.Should().Be(DeliveryStatus.NotScheduled);
            updated.DeliveryVehicleId.Should().BeNull();
            updated.DeliveryShift.Should().BeNull();
            updated.ScheduledDeliveryDate.Should().BeNull();
        }

        // DEL-07 | EP-Invalid | Xuất phát khi còn đơn chưa có HandoverRecord Confirmed -> chặn
        [Fact]
        public async Task DEL_07_StartTrip_MissingConfirmedHandover_Rejected()
        {
            var vehicle = SeedVehicle(1000m);
            var order = SeedPackedOrder(300m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Loading };
            trip.Orders.Add(order);
            order.DeliveryTripId = trip.Id;
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();
            // Không seed HandoverRecord Confirmed cho đơn này.

            var act = () => _sut.StartTripAsync(trip.Id);

            await act.Should().ThrowAsync<HandoverNotReadyException>();
            _db.DeliveryTrips.Single(t => t.Id == trip.Id).Status.Should().Be(DeliveryTripStatus.Loading);
        }

        // DEL-08 | State-Valid | Mọi đơn đã Handover Confirmed -> Loading chuyển InDelivery, đơn -> InDelivery
        [Fact]
        public async Task DEL_08_StartTrip_AllHandoversConfirmed_MovesToInDelivery()
        {
            var vehicle = SeedVehicle(1000m);
            var order = SeedPackedOrder(300m);
            var trip = new DeliveryTrip { VehicleId = vehicle.Id, Shift = "Sáng", TripDate = DateTime.UtcNow.Date, Status = DeliveryTripStatus.Loading };
            trip.Orders.Add(order);
            order.DeliveryTripId = trip.Id;
            _db.DeliveryTrips.Add(trip);
            _db.SaveChanges();
            SeedConfirmedHandover(order.Id);

            var result = await _sut.StartTripAsync(trip.Id);

            result.Status.Should().Be(nameof(DeliveryTripStatus.InDelivery));
            _db.Orders.Single(o => o.Id == order.Id).DeliveryStatus.Should().Be(DeliveryStatus.InDelivery);
        }
    }
}
