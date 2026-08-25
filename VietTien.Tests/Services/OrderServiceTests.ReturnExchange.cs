using FluentAssertions;
using Moq;
using VietTien.API.DTOs.Delivery;
using VietTien.API.DTOs.Order;
using VietTien.API.Exceptions;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — ⊕ v2.1 block Đổi/Trả hàng &amp; Thu hồi hàng (L1-ORD-48..60).
    /// Dùng chung fixture với OrderServiceTests (partial class).
    ///
    /// ⚠ L1-ORD-53 (Sales Staff tự duyệt yêu cầu đổi/trả -> forbidden): ProcessReturnExchangeRequestAsync
    ///    (requestId, managerId, dto) không nhận vai trò; chặn nằm ở [Authorize(Roles="SalesManager,Admin")]
    ///    trên OrderController -> đã chuyển sang L3: VietTien.IntegrationTests/RoleGateTests.cs (L1_ORD_53_*).
    /// ⚠ ConfirmPickupAsync(requestId, userId) KHÔNG nhận dto{qty} như doc mô tả.
    /// </summary>
    public partial class OrderServiceTests
    {
        /// <summary>Đơn đã giao thành công (điều kiện bắt buộc để tạo yêu cầu đổi/trả), kèm 1 dòng hàng.</summary>
        private (Order order, Product product) SeedDeliveredOrder(int deliveredQty = 5, decimal unitPrice = 50_000m, Guid? customerProfileId = null)
        {
            var product = TestData.SeedProduct(_db, p => p.StandardListedPrice = unitPrice);
            var order = TestData.Order(customerProfileId ?? _profile.Id, o =>
            {
                o.OrderStatus = OrderStatus.Completed;
                o.DeliveryStatus = DeliveryStatus.Delivered;
                o.PaymentStatus = PaymentStatus.Paid;
            });
            _db.Orders.Add(order);
            _db.SaveChanges();
            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, deliveredQty, unitPrice));
            _db.SaveChanges();
            return (order, product);
        }

        private static CreateReturnExchangeRequestDto ReturnDto(Guid productId, int qty, Guid? exchangeForProductId = null)
        {
            var dto = new CreateReturnExchangeRequestDto
            {
                Reason = "Hàng bị nứt khi nhận",
                EvidenceUrls = "[\"https://cdn/evidence-1.jpg\"]",
                ReturnItems = { new ReturnExchangeItemDto { ProductId = productId, Quantity = qty } }
            };
            if (exchangeForProductId.HasValue)
                dto.ExchangeItems.Add(new ReturnExchangeNewItemDto { ProductId = exchangeForProductId.Value, Quantity = qty });
            return dto;
        }

        private ReturnExchangeRequest LatestRequest()
        {
            _db.ChangeTracker.Clear();
            return _db.ReturnExchangeRequests.OrderByDescending(r => r.CreatedAt).First();
        }

        // ── Block: Đổi/Trả hàng ─────────────────────────────────────────────

        // L1-ORD-48 | EP-Valid | Tạo yêu cầu đổi/trả cho dòng đã giao -> lưu chờ duyệt, KHÔNG đụng tồn kho
        [Fact]
        public async Task L1_ORD_48_CreateReturnRequest_StoresPendingWithoutTouchingStock()
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);
            var inventory = TestData.SeedInventory(_db, product.Id, 100);

            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 2));

            var request = LatestRequest();
            request.Status.Should().Be(ReturnExchangeStatus.Pending);
            request.OrderId.Should().Be(order.Id);
            _db.ReturnExchangeRequestItems.Single(i => i.ReturnExchangeRequestId == request.Id).Quantity.Should().Be(2);
            _db.Inventories.Single(i => i.Id == inventory.Id).OnHandQuantity.Should().Be(100, "bước tạo yêu cầu chưa được đụng tồn kho");
        }

        // L1-ORD-49 | EP-Invalid + BVA-Max+1 | Số lượng trả vượt số đã giao -> từ chối; đúng bằng thì hợp lệ
        [Theory]
        [InlineData(6, true)]  // > 5 đã giao -> từ chối
        [InlineData(5, false)] // đúng bằng số đã giao -> hợp lệ
        public async Task L1_ORD_49_ReturnQuantityBoundary(int requestedQty, bool shouldReject)
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);

            var act = () => _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, requestedQty));

            if (shouldReject)
            {
                await act.Should().ThrowAsync<InvalidOperationException>();
                _db.ReturnExchangeRequests.Should().BeEmpty();
            }
            else
            {
                await act.Should().NotThrowAsync();
                _db.ReturnExchangeRequests.Should().ContainSingle();
            }
        }

        // L1-ORD-50 | EP-Invalid | Tạo yêu cầu trên đơn CHƯA giao xong -> state guard chặn
        [Fact]
        public async Task L1_ORD_50_CreateReturnRequest_OnUndeliveredOrder_IsRejected()
        {
            var product = TestData.SeedProduct(_db);
            var order = SeedOrder(o =>
            {
                o.OrderStatus = OrderStatus.Confirmed;
                o.DeliveryStatus = DeliveryStatus.InDelivery; // chưa giao xong
            });
            _db.OrderItems.Add(TestData.OrderItem(order.Id, product.Id, 5));
            _db.SaveChanges();

            var act = () => _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 1));

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ReturnExchangeRequests.Should().BeEmpty();
        }

        // L1-ORD-51 | EP-Invalid | Khách tạo yêu cầu trên đơn của KHÁCH KHÁC -> forbidden
        // 🔴 SPEC GAP v2.2: CreateReturnExchangeRequestAsync chỉ GHI LẠI requestedByUserId, không đối chiếu
        // với order.CustomerProfile.UserId -> IDOR. Controller cho phép cả role Customer nên guard này
        // phải nằm ở service. Test ĐỎ cho tới khi bổ sung kiểm tra sở hữu (FT-08 NAC-05; NFR-SEC03).
        [Fact]
        public async Task L1_ORD_51_CreateReturnRequest_OnAnotherCustomersOrder_IsForbidden()
        {
            var (otherUser, otherProfile) = TestData.SeedCustomer(_db);
            var (foreignOrder, product) = SeedDeliveredOrder(deliveredQty: 5, customerProfileId: otherProfile.Id);

            var act = () => _sut.CreateReturnExchangeRequestAsync(foreignOrder.Id, _customer.Id, ReturnDto(product.Id, 1));

            await act.Should().ThrowAsync<Exception>("không được thao tác trên đơn của khách khác");
            _db.ReturnExchangeRequests.Should().BeEmpty();
        }

        // L1-ORD-52 | State-Valid | Manager duyệt -> Approved, đơn gốc chuyển Returned
        [Fact]
        public async Task L1_ORD_52_ApproveRequest_MovesToApprovedAndFlagsOrder()
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);
            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 2));
            var requestId = LatestRequest().Id;

            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id,
                new ProcessReturnExchangeRequestDto { IsApproved = true, ManagerNote = "Đồng ý thu hồi" });

            _db.ChangeTracker.Clear();
            var request = _db.ReturnExchangeRequests.Single(r => r.Id == requestId);
            request.Status.Should().Be(ReturnExchangeStatus.Approved);
            request.ProcessedByUserId.Should().Be(_salesStaff.Id);
            _db.Orders.Single(o => o.Id == order.Id).OrderStatus.Should().Be(OrderStatus.Returned);
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_55_ReturnExchangeRequestResult, _customer.Id,
                It.IsAny<string>(), It.IsAny<string>(), requestId, "ReturnExchangeRequest"), Times.Once);
        }

        // L1-ORD-52b | Notify | Từ chối yêu cầu đổi/trả -> báo khách hàng kèm lý do (ManagerNote)
        [Fact]
        public async Task L1_ORD_52b_RejectRequest_NotifiesCustomerWithReason()
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);
            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 2));
            var requestId = LatestRequest().Id;

            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id,
                new ProcessReturnExchangeRequestDto { IsApproved = false, ManagerNote = "Hàng không lỗi" });

            _db.ChangeTracker.Clear();
            _db.ReturnExchangeRequests.Single(r => r.Id == requestId).Status.Should().Be(ReturnExchangeStatus.Rejected);
            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_55_ReturnExchangeRequestResult, _customer.Id,
                It.IsAny<string>(), It.Is<string>(b => b.Contains("Hàng không lỗi")), requestId, "ReturnExchangeRequest"), Times.Once);
        }

        // L1-ORD-54 | State-Invalid | Duyệt lại yêu cầu đã ở trạng thái kết thúc -> conflict
        [Fact]
        public async Task L1_ORD_54_ProcessRequest_Twice_IsConflict()
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);
            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 2));
            var requestId = LatestRequest().Id;
            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id, new ProcessReturnExchangeRequestDto { IsApproved = true });

            var act = () => _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id, new ProcessReturnExchangeRequestDto { IsApproved = true });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // L1-ORD-55 | EP-Valid | Đổi CÙNG mã hàng -> đơn thay thế giữ nguyên đơn giá gốc, không phát sinh chênh lệch
        [Fact]
        public async Task L1_ORD_55_ExchangeSameProduct_KeepsOriginalUnitPrice()
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5, unitPrice: 50_000m);
            TestData.SeedInventory(_db, product.Id, 100);
            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, 2, exchangeForProductId: product.Id));
            var requestId = LatestRequest().Id;
            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id, new ProcessReturnExchangeRequestDto { IsApproved = true });

            // Giá hiện hành tăng SAU khi đơn gốc chốt — đơn thay thế cùng mã hàng không được áp giá mới
            _db.Products.Single(p => p.Id == product.Id).StandardListedPrice = 90_000m;
            _db.SaveChanges();

            var replacement = await _sut.CreateExchangeReplacementOrderAsync(requestId, _salesStaff.Id);

            _db.ChangeTracker.Clear();
            var replacementItems = _db.OrderItems.Where(oi => oi.OrderId == replacement.ReplacementOrderId).ToList();
            replacementItems.Should().ContainSingle();
            replacementItems[0].PriceSnapshot.Should().Be(50_000m, "đổi cùng mã hàng giữ nguyên đơn giá gốc");
        }

        // L1-ORD-56 | EP-Valid | Đổi SANG mã hàng khác -> áp giá hiện hành của mã mới
        [Fact]
        public async Task L1_ORD_56_ExchangeDifferentProduct_UsesCurrentPrice()
        {
            var (order, oldProduct) = SeedDeliveredOrder(deliveredQty: 5, unitPrice: 50_000m);
            var newProduct = TestData.SeedProduct(_db, p => p.StandardListedPrice = 70_000m);
            TestData.SeedInventory(_db, newProduct.Id, 100);

            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(oldProduct.Id, 2, exchangeForProductId: newProduct.Id));
            var requestId = LatestRequest().Id;
            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id, new ProcessReturnExchangeRequestDto { IsApproved = true });

            var replacement = await _sut.CreateExchangeReplacementOrderAsync(requestId, _salesStaff.Id);

            _db.ChangeTracker.Clear();
            var item = _db.OrderItems.Single(oi => oi.OrderId == replacement.ReplacementOrderId);
            item.ProductId.Should().Be(newProduct.Id);
            item.PriceSnapshot.Should().Be(70_000m, "mã hàng khác thì áp giá hiện hành");
        }

        // ── Block: Thu hồi hàng ─────────────────────────────────────────────

        /// <summary>Tạo + duyệt 1 yêu cầu đổi/trả, trả về requestId đã ở trạng thái Approved.</summary>
        private async Task<(Guid requestId, Product product)> SeedApprovedRequestAsync(int returnQty = 2)
        {
            var (order, product) = SeedDeliveredOrder(deliveredQty: 5);
            await _sut.CreateReturnExchangeRequestAsync(order.Id, _customer.Id, ReturnDto(product.Id, returnQty));
            var requestId = LatestRequest().Id;
            await _sut.ProcessReturnExchangeRequestAsync(requestId, _salesStaff.Id, new ProcessReturnExchangeRequestDto { IsApproved = true });
            return (requestId, product);
        }

        // L1-ORD-57 | EP-Valid | Danh sách chờ thu hồi chỉ chứa yêu cầu ĐÃ DUYỆT và CHƯA thu xong
        [Fact]
        public async Task L1_ORD_57_PendingPickups_OnlyApprovedAndNotYetPickedUp()
        {
            SeedFleet();
            // (1) chờ duyệt
            var (pendingOrder, p1) = SeedDeliveredOrder(deliveredQty: 5);
            await _sut.CreateReturnExchangeRequestAsync(pendingOrder.Id, _customer.Id, ReturnDto(p1.Id, 1));

            // (2) đã duyệt, chưa thu
            var (approvedId, _) = await SeedApprovedRequestAsync();

            // (3) đã duyệt và đã thu xong
            var (pickedUpId, _) = await SeedApprovedRequestAsync();
            await _sut.SchedulePickupAsync(pickedUpId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 1, Shift = "Sáng", PickupDate = DateTime.UtcNow.AddDays(1) });
            await _sut.ConfirmPickupAsync(pickedUpId, _salesStaff.Id);

            var pending = await _sut.GetPendingPickupsAsync(_salesStaff.Id);

            pending.Should().ContainSingle().Which.RequestId.Should().Be(approvedId);
        }

        // L1-ORD-58 | EP-Valid | Lên lịch thu hồi -> lưu ngày, ca và xe phụ trách
        [Fact]
        public async Task L1_ORD_58_SchedulePickup_StoresDateShiftAndVehicle()
        {
            SeedFleet();
            var (requestId, _) = await SeedApprovedRequestAsync();
            var pickupDate = DateTime.UtcNow.AddDays(2).Date;

            await _sut.SchedulePickupAsync(requestId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 3, Shift = "Chiều", PickupDate = pickupDate });

            _db.ChangeTracker.Clear();
            var request = _db.ReturnExchangeRequests.Single(r => r.Id == requestId);
            request.PickupStatus.Should().Be(PickupStatus.Scheduled);
            request.PickupVehicleId.Should().Be(3);
            request.PickupShift.Should().Be("Chiều");
            request.ScheduledPickupDate.Should().Be(pickupDate);
        }

        // L1-ORD-58b | Notify | Lên lịch thu hồi -> báo KHÁCH HÀNG (ngày/ca xe đến lấy) và báo cả
        // team KHO (WarehouseStaff) để chuẩn bị tiếp nhận hàng thu hồi.
        [Fact]
        public async Task L1_ORD_58b_SchedulePickup_NotifiesCustomerAndWarehouseRole()
        {
            SeedFleet();
            var (requestId, _) = await SeedApprovedRequestAsync();
            var pickupDate = DateTime.UtcNow.AddDays(2).Date;

            await _sut.SchedulePickupAsync(requestId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 3, Shift = "Chiều", PickupDate = pickupDate });

            _noti.Verify(n => n.CreateNotificationAsync(
                NotificationType.SYS_56_PickupScheduled, _customer.Id,
                It.IsAny<string>(), It.IsAny<string>(), requestId, "ReturnExchangeRequest"), Times.Once);
            _noti.Verify(n => n.CreateRoleNotificationAsync(
                NotificationType.SYS_57_PickupTaskForWarehouse, SystemRole.WarehouseStaff,
                It.IsAny<string>(), It.IsAny<string>(), requestId, "ReturnExchangeRequest"), Times.Once);
        }

        // L1-ORD-59 | EP-Valid | Xác nhận đã thu hồi -> hàng KHÔNG được cộng vào tồn bán được
        // (hàng trả về phải đi vào khu cách ly/kiểm định trước — BR-019, BR-032).
        [Fact]
        public async Task L1_ORD_59_ConfirmPickup_DoesNotIncreaseAvailableStock()
        {
            SeedFleet();
            var (requestId, product) = await SeedApprovedRequestAsync(returnQty: 2);
            var inventory = TestData.SeedInventory(_db, product.Id, 10);
            await _sut.SchedulePickupAsync(requestId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 1, Shift = "Sáng", PickupDate = DateTime.UtcNow.AddDays(1) });

            await _sut.ConfirmPickupAsync(requestId, _salesStaff.Id);

            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == inventory.Id).AvailableQuantity
                .Should().Be(10, "hàng thu hồi chưa kiểm định thì không được bán");
            _db.ReturnExchangeRequests.Single(r => r.Id == requestId).PickupStatus.Should().Be(PickupStatus.PickedUp);
        }

        // L1-ORD-60 | EP-Invalid | Xác nhận thu hồi khi CHƯA lên lịch -> state guard chặn
        [Fact]
        public async Task L1_ORD_60_ConfirmPickup_WithoutSchedule_IsRejected()
        {
            var (requestId, product) = await SeedApprovedRequestAsync();
            var inventory = TestData.SeedInventory(_db, product.Id, 10);

            var act = () => _sut.ConfirmPickupAsync(requestId, _salesStaff.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.ChangeTracker.Clear();
            _db.Inventories.Single(i => i.Id == inventory.Id).OnHandQuantity.Should().Be(10);
        }

        // ── Trọng lượng xe cho thu hồi hàng trả về kiểm định ─────────────────

        // L1-ORD-61 | EP-Invalid | Xe không tồn tại/inactive -> từ chối điều xe
        [Fact]
        public async Task L1_ORD_61_SchedulePickup_InactiveOrMissingVehicle_Rejected()
        {
            var (requestId, _) = await SeedApprovedRequestAsync();

            var act = () => _sut.SchedulePickupAsync(requestId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 9, Shift = "Sáng", PickupDate = DateTime.UtcNow.AddDays(1) });

            await act.Should().ThrowAsync<Exception>().WithMessage("Mã xe không hợp lệ hoặc xe đã ngừng hoạt động.");
        }

        // L1-ORD-62 | EP-Invalid | Yêu cầu thu hồi vượt tải trọng xe -> VehicleOverweightException
        [Fact]
        public async Task L1_ORD_62_SchedulePickup_OverCapacity_Rejected()
        {
            _db.Vehicles.Add(new Vehicle { VehicleNumber = 1, LicensePlate = "29C-001", IsActive = true, Capacity = 50m });
            _db.SaveChanges();
            var (requestId, product) = await SeedApprovedRequestAsync(returnQty: 2);
            // SeedApprovedRequestAsync() gọi LatestRequest() nội bộ -> ChangeTracker.Clear() -> `product`
            // đã bị detach, phải query lại bản tracked mới rồi mới sửa để SaveChanges không no-op.
            _db.Products.Single(p => p.Id == product.Id).WeightKg = 30m; // 2 x 30kg = 60kg > 50kg
            _db.SaveChanges();

            var act = () => _sut.SchedulePickupAsync(requestId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 1, Shift = "Sáng", PickupDate = DateTime.UtcNow.AddDays(1) });

            await act.Should().ThrowAsync<VehicleOverweightException>();
            _db.ReturnExchangeRequests.Single(r => r.Id == requestId).PickupStatus.Should().Be(PickupStatus.NotScheduled);
        }

        // L1-ORD-63 | EP-Invalid | Vượt tải CỘNG DỒN qua 2 yêu cầu cùng xe/ca/ngày
        [Fact]
        public async Task L1_ORD_63_SchedulePickup_CumulativeAcrossRequests_Rejected()
        {
            _db.Vehicles.Add(new Vehicle { VehicleNumber = 1, LicensePlate = "29C-001", IsActive = true, Capacity = 50m });
            _db.SaveChanges();
            var pickupDate = DateTime.UtcNow.AddDays(1);

            var (firstId, firstProduct) = await SeedApprovedRequestAsync(returnQty: 1);
            _db.Products.Single(p => p.Id == firstProduct.Id).WeightKg = 30m; // fits alone
            _db.SaveChanges();
            await _sut.SchedulePickupAsync(firstId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 1, Shift = "Sáng", PickupDate = pickupDate });

            var (secondId, secondProduct) = await SeedApprovedRequestAsync(returnQty: 1);
            _db.Products.Single(p => p.Id == secondProduct.Id).WeightKg = 30m; // 30kg + 30kg = 60kg > 50kg
            _db.SaveChanges();
            var act = () => _sut.SchedulePickupAsync(secondId, _salesStaff.Id,
                new SchedulePickupRequestDto { VehicleId = 1, Shift = "Sáng", PickupDate = pickupDate });

            await act.Should().ThrowAsync<VehicleOverweightException>();
            _db.ReturnExchangeRequests.Single(r => r.Id == secondId).PickupStatus.Should().Be(PickupStatus.NotScheduled);
        }
    }
}
