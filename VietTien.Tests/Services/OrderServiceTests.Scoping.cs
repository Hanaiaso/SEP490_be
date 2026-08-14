using FluentAssertions;
using VietTien.API.Models;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: OrderService — ⊕ v2.1 block Đổi chữ ký (thêm tham số caller) và Truy vấn công khai
    /// (L1-ORD-61..70). Dùng chung fixture với OrderServiceTests (partial class).
    ///
    /// L1-ORD-71 (GetSalesOrderDetail: Sales xem đơn ngoài phạm vi -> forbidden): đã fix — thêm tham số
    /// tuỳ chọn `salesStaffId` cho GetSalesOrderDetailAsync, scope theo snapshot `order.SalesStaffId`
    /// (cùng cơ chế đã dùng ở GetSalesOrdersAsync). Role-gate (Customer/Warehouse bị chặn hẳn) vẫn ở L3:
    /// VietTien.IntegrationTests/RoleGateTests.cs (L1_ORD_71_*).
    /// </summary>
    public partial class OrderServiceTests
    {
        private const string RoleCustomer = "Customer";
        private const string RoleSalesManager = "SalesManager";

        /// <summary>Đơn của MỘT KHÁCH KHÁC (U9) — dùng cho các case truy cập chéo.</summary>
        private (Order order, User user) SeedForeignCustomerOrder(Action<Order>? mutate = null)
        {
            var (foreignUser, foreignProfile) = TestData.SeedCustomer(_db);
            var order = TestData.Order(foreignProfile.Id, mutate);
            _db.Orders.Add(order);
            _db.SaveChanges();
            return (order, foreignUser);
        }

        // ── Block: Đổi chữ ký method (thêm tham số caller) ──────────────────

        // L1-ORD-61 | EP-Invalid | GenerateSePayQr trên đơn KHÔNG thuộc mình -> forbidden, không sinh QR
        [Fact]
        public async Task L1_ORD_61_GenerateSePayQr_CrossCustomer_IsForbidden()
        {
            var (foreignOrder, _) = SeedForeignCustomerOrder();

            var act = () => _sut.GenerateSePayQrAsync(foreignOrder.Id, _customer.Id, RoleCustomer);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // L1-ORD-61 (nhánh hợp lệ) | EP-Valid | Chủ đơn tự sinh QR -> thành công, nội dung CK = mã đơn
        [Fact]
        public async Task L1_ORD_61_GenerateSePayQr_Owner_Succeeds()
        {
            var order = SeedOrder(o => o.PaymentMethod = PaymentMethod.SePay);

            var qr = await _sut.GenerateSePayQrAsync(order.Id, _customer.Id, RoleCustomer);

            qr.TransferContent.Should().Be(order.OrderCode);
            qr.QrImageUrl.Should().Contain(order.OrderCode);
        }

        // L1-ORD-62 | EP-Invalid | GetPaymentStatus truy cập chéo khách hàng -> forbidden
        [Fact]
        public async Task L1_ORD_62_GetPaymentStatus_CrossCustomer_IsForbidden()
        {
            var (foreignOrder, _) = SeedForeignCustomerOrder();

            var act = () => _sut.GetPaymentStatusAsync(foreignOrder.Id, _customer.Id, RoleCustomer);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // L1-ORD-63 | EP-Valid | Sales Manager (vai trò nội bộ) xem được đơn ngoài phạm vi khách
        [Fact]
        public async Task L1_ORD_63_GetPaymentStatus_InternalRole_IsAllowed()
        {
            var (foreignOrder, _) = SeedForeignCustomerOrder(o => o.PaymentStatus = PaymentStatus.Paid);

            var status = await _sut.GetPaymentStatusAsync(foreignOrder.Id, _salesStaff.Id, RoleSalesManager);

            status.Status.Should().Be(nameof(PaymentStatus.Paid));
        }

        // L1-ORD-64 | EP-Invalid | UploadInvoicePdf bởi khách không sở hữu đơn -> forbidden, không lưu file
        // ⚠ Signature thật nhận string pdfBase64 (không phải IFormFile) như doc mô tả.
        [Fact]
        public async Task L1_ORD_64_UploadInvoicePdf_WrongCaller_IsForbidden()
        {
            var (foreignOrder, _) = SeedForeignCustomerOrder();

            var act = () => _sut.UploadInvoicePdfAsync(foreignOrder.Id, "JVBERi0xLjQK", _customer.Id, RoleCustomer);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
            _db.ChangeTracker.Clear();
            _db.Orders.Single(o => o.Id == foreignOrder.Id).InvoicePdfUrl.Should().BeNull("file không được lưu");
        }

        // L1-ORD-65 | EP-Valid | GetSalesDashboardStats với scopedSalesStaffId -> chỉ tính đơn của Sales đó
        [Fact]
        public async Task L1_ORD_65_SalesDashboardStats_ScopedToSingleStaff()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            _db.SaveChanges();

            for (var i = 0; i < 3; i++)
                SeedOrder(o => { o.SalesStaffId = _salesStaff.Id; o.OrderStatus = OrderStatus.Draft; });
            for (var i = 0; i < 2; i++)
                SeedOrder(o => { o.SalesStaffId = otherSales.Id; o.OrderStatus = OrderStatus.Draft; });

            var stats = await _sut.GetSalesDashboardStatsAsync(_salesStaff.Id);

            stats.Kpi.NewOrdersCount.Should().Be(3, "không lẫn 2 đơn của Sales còn lại");
        }

        // L1-ORD-66 | EP-Valid | scopedSalesStaffId = null -> phạm vi toàn nhóm (Manager)
        [Fact]
        public async Task L1_ORD_66_SalesDashboardStats_NullScope_CoversWholeTeam()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            _db.SaveChanges();

            for (var i = 0; i < 3; i++)
                SeedOrder(o => { o.SalesStaffId = _salesStaff.Id; o.OrderStatus = OrderStatus.Draft; });
            for (var i = 0; i < 2; i++)
                SeedOrder(o => { o.SalesStaffId = otherSales.Id; o.OrderStatus = OrderStatus.Draft; });

            var stats = await _sut.GetSalesDashboardStatsAsync(scopedSalesStaffId: null);

            stats.Kpi.NewOrdersCount.Should().Be(5, "đủ 5 đơn của cả nhóm");
        }

        // L1-ORD-65b | EP-Valid | "Công nợ cần thu" phải lấy từ sổ công nợ thật (CustomerDebts, InDebt),
        // KHÔNG được cộng FinalPayment của đơn PaymentStatus=Pending (đơn đang chờ/chưa từng thanh toán,
        // kể cả đã bị hủy — không phải khoản khách nợ).
        //
        // 🔴 DEFECT (đã sửa): OrderService.cs cũ tính `pendingDebt = Sum(FinalPayment) WHERE
        // PaymentStatus=Pending`, khiến dashboard hiện "nợ" ảo cho cả đơn đã Cancelled do hết hạn giữ
        // chỗ dù sổ công nợ thật đang là 0đ (phát hiện qua đối chiếu dữ liệu production).
        [Fact]
        public async Task L1_ORD_65b_SalesDashboardStats_PendingDebt_UsesRealDebtLedger()
        {
            // Đơn chỉ đang CHỜ thanh toán (chưa từng có tiền vào) -> KHÔNG tính là nợ.
            SeedOrder(o => { o.SalesStaffId = _salesStaff.Id; o.PaymentStatus = PaymentStatus.Pending; o.FinalPayment = 50_000_000m; });
            // Đơn đã bị hủy do hết hạn giữ chỗ, vẫn còn PaymentStatus=Pending -> càng không phải nợ.
            SeedOrder(o => { o.SalesStaffId = _salesStaff.Id; o.PaymentStatus = PaymentStatus.Pending; o.OrderStatus = OrderStatus.Cancelled; o.FinalPayment = 20_000_000m; });

            // Đơn THẬT SỰ còn nợ (khách trả thiếu qua COD) -> phải tính vào PendingDebt.
            var debtOrder = SeedOrder(o => { o.SalesStaffId = _salesStaff.Id; o.PaymentStatus = PaymentStatus.PartiallyPaid; o.FinalPayment = 1_000_000m; o.AmountPaid = 700_000m; });
            _db.CustomerDebts.Add(new CustomerDebt
            {
                CustomerProfileId = debtOrder.CustomerProfileId,
                OrderId = debtOrder.Id,
                DebtAmount = 300_000m,
                Status = DebtStatus.InDebt,
            });
            _db.SaveChanges();

            var stats = await _sut.GetSalesDashboardStatsAsync(_salesStaff.Id);

            stats.Kpi.PendingDebt.Should().Be(300_000m,
                "chỉ tính khoản nợ THẬT (CustomerDebts InDebt), không cộng FinalPayment của đơn đang chờ/đã hủy");
        }

        // ── Block: Truy vấn công khai & thống kê ────────────────────────────

        // L1-ORD-67 | EP-Valid | Tra cứu công khai bằng mã đơn -> chỉ trả thông tin KHÔNG nhạy cảm
        // 🔴 SPEC GAP v2.2: TrackOrderPublicAsync trả nguyên OrderHistoryDetailDto, gồm CustomerPhone và
        // ShippingAddress đầy đủ — endpoint này là công khai (chỉ cần biết mã đơn). NFR-SEC06 yêu cầu
        // mask PII. Test ĐỎ cho tới khi che bớt các trường nhạy cảm ở luồng tra cứu công khai.
        [Fact]
        public async Task L1_ORD_67_TrackOrderPublic_DoesNotExposePii()
        {
            var order = SeedOrder(o => o.OrderStatus = OrderStatus.Completed);

            var result = await _sut.TrackOrderPublicAsync(order.OrderCode);

            result.OrderCode.Should().Be(order.OrderCode);
            result.OrderStatus.Should().NotBeNullOrEmpty("trạng thái và mốc thời gian là thông tin được phép trả");

            result.CustomerPhone.Should().BeNull("số điện thoại đầy đủ không được lộ qua tra cứu công khai");
            result.ShippingAddress.Should().BeNull("địa chỉ giao hàng đầy đủ không được lộ qua tra cứu công khai");
        }

        // L1-ORD-68 | EP-Invalid | Tra cứu mã đơn KHÔNG tồn tại -> not-found chung
        [Fact]
        public async Task L1_ORD_68_TrackOrderPublic_UnknownCode_ReturnsNotFound()
        {
            var act = () => _sut.TrackOrderPublicAsync("ORD-9999");

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        // L1-ORD-69 | EP-Valid | Thống kê chi tiêu chỉ tính đơn ĐÃ THANH TOÁN của CHÍNH khách đó
        [Fact]
        public async Task L1_ORD_69_SpendingStats_ScopedToCallerAndExcludesCancelled()
        {
            SeedOrder(o => { o.OrderStatus = OrderStatus.Completed; o.PaymentStatus = PaymentStatus.Paid; o.FinalPayment = 1_000_000m; });
            SeedOrder(o => { o.OrderStatus = OrderStatus.Completed; o.PaymentStatus = PaymentStatus.Paid; o.FinalPayment = 2_000_000m; });
            SeedOrder(o => { o.OrderStatus = OrderStatus.Cancelled; o.PaymentStatus = PaymentStatus.Unpaid; o.FinalPayment = 9_000_000m; });
            SeedForeignCustomerOrder(o => { o.OrderStatus = OrderStatus.Completed; o.PaymentStatus = PaymentStatus.Paid; o.FinalPayment = 7_000_000m; });

            var stats = await _sut.GetSpendingStatsAsync(_customer.Id, "month");

            stats.TotalOrders.Should().Be(2);
            stats.TotalSpent.Should().Be(3_000_000m, "loại đơn huỷ và đơn của khách khác");
        }

        // L1-ORD-70 | EP-Valid | Danh sách đơn giao cho Sales chỉ gồm đơn của khách do người đó phụ trách
        [Fact]
        public async Task L1_ORD_70_DeliveryOrders_ScopedToAssignedCustomers()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            var (_, otherProfile) = TestData.SeedCustomer(_db);
            otherProfile.AssignedSalesStaffId = otherSales.Id;
            _db.SaveChanges();

            // Đơn của khách do _salesStaff phụ trách
            var mine = SeedOrder(o => o.DeliveryStatus = DeliveryStatus.Scheduled);
            // Đơn của khách do Sales khác phụ trách
            var theirs = TestData.Order(otherProfile.Id, o => o.DeliveryStatus = DeliveryStatus.Scheduled);
            _db.Orders.Add(theirs);
            _db.SaveChanges();

            var result = await _sut.GetDeliveryOrdersAsync(_salesStaff.Id);

            result.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        }

        // L1-ORD-71 | EP-Invalid | GetSalesOrderDetail: Sales xem đơn của khách do Sales KHÁC phụ trách -> forbidden
        [Fact]
        public async Task L1_ORD_71_GetSalesOrderDetail_OtherSalesStaffOrder_IsForbidden()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            _db.SaveChanges();
            var theirOrder = SeedOrder(o => o.SalesStaffId = otherSales.Id);

            var act = () => _sut.GetSalesOrderDetailAsync(theirOrder.Id, _salesStaff.Id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // L1-ORD-71 (nhánh hợp lệ) | EP-Valid | Sales xem đơn của chính khách mình phụ trách -> thành công
        [Fact]
        public async Task L1_ORD_71_GetSalesOrderDetail_OwnOrder_Succeeds()
        {
            var order = SeedOrder(o => o.SalesStaffId = _salesStaff.Id);

            var result = await _sut.GetSalesOrderDetailAsync(order.Id, _salesStaff.Id);

            result.Id.Should().Be(order.Id);
        }

        // L1-ORD-71 (nhánh hợp lệ) | EP-Valid | salesStaffId = null (Manager/Admin) -> xem được mọi đơn
        [Fact]
        public async Task L1_ORD_71_GetSalesOrderDetail_NullScope_SeesAnyOrder()
        {
            var otherSales = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(otherSales);
            _db.SaveChanges();
            var theirOrder = SeedOrder(o => o.SalesStaffId = otherSales.Id);

            var result = await _sut.GetSalesOrderDetailAsync(theirOrder.Id, salesStaffId: null);

            result.Id.Should().Be(theirOrder.Id);
        }
    }
}
