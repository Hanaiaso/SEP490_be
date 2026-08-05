using FluentAssertions;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: DashboardKpiServices — L1-DASH-01, 03, 04, 06, 07, 08.
    /// CeoDashboardService · SalesManagerDashboardService · SalesStaffDashboardService · KpiService.
    ///
    /// ⚠ L1-DASH-02 và L1-DASH-05 là phân quyền, không phải logic service -> không unit-test được ở
    ///   đây (đã chuyển sang L3: VietTien.IntegrationTests/RoleGateTests.cs, L1_DASH_02_*/L1_DASH_05_*):
    ///   • DASH-02: ISalesStaffDashboardService.GetDashboardAsync(callerId, from, to) — callerId luôn
    ///     lấy từ JWT ở Controller, Sales Staff không có đường truyền id người khác vào service.
    ///   • DASH-05: ISalesManagerDashboardService.GetDashboardAsync(from, to) — không hề có tham số
    ///     caller; chặn vai trò nằm ở [Authorize] tầng Controller.
    ///
    /// ⚠ Tên method thật là GetDashboardAsync(...) chứ không phải GetAsync(id, period) như doc mô tả.
    /// ⚠ KpiService tính ProcessingSpeedAvgHours bằng EF.Functions.DateDiffMinute (chỉ có trên SQL Server)
    ///   — mọi order seed trong file này để ConfirmedAt = null để không chạm nhánh đó trên EF InMemory.
    /// ⚠ Doanh thu tính trên OrderStatus.Completed (OrderStatus / DeliveryStatus là 2 enum TÁCH BIỆT).
    /// </summary>
    public class DashboardKpiServicesTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly KpiService _kpi;

        private readonly DateTime _from = DateTime.UtcNow.Date.AddDays(-7);
        private readonly DateTime _to = DateTime.UtcNow.Date.AddDays(1);

        public DashboardKpiServicesTests()
        {
            _kpi = new KpiService(_db);
        }

        private User SeedSales()
        {
            var user = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.Add(user);
            _db.SaveChanges();
            return user;
        }

        /// <summary>Seed 1 đơn đã hoàn tất gán cho 1 Sales. ConfirmedAt để null (xem ghi chú class).</summary>
        private Order SeedCompletedOrder(Guid salesStaffId, decimal amount = 1_000_000m, DateTime? createdAt = null)
        {
            var (_, profile) = TestData.SeedCustomer(_db);
            var order = TestData.Order(profile.Id, o =>
            {
                o.SalesStaffId = salesStaffId;
                o.OrderStatus = OrderStatus.Completed;
                o.FinalPayment = amount;
                o.CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-1);
                o.ConfirmedAt = null;
            });
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        // ── Block: Phân quyền phạm vi dữ liệu ───────────────────────────────

        // L1-DASH-01 | EP-Valid | Sales Staff chỉ thấy số liệu đơn của chính mình, không lẫn của người khác
        [Fact]
        public async Task L1_DASH_01_SalesStaffDashboard_IsScopedToCaller()
        {
            var s1 = SeedSales();
            var s2 = SeedSales();
            for (var i = 0; i < 5; i++) SeedCompletedOrder(s1.Id, 1_000_000m);
            for (var i = 0; i < 4; i++) SeedCompletedOrder(s2.Id, 2_000_000m);

            var sut = new SalesStaffDashboardService(_kpi);
            var dashboard = await sut.GetDashboardAsync(s1.Id, _from, _to);

            dashboard.Kpi.CompletedOrderCount.Should().Be(5, "chỉ 5 đơn của S1");
            dashboard.Kpi.Revenue.Should().Be(5_000_000m, "không lẫn doanh thu 8tr của S2");
            dashboard.Kpi.SalesStaffId.Should().Be(s1.Id);
        }

        // L1-DASH-03 | EP-Valid | Sales Manager thấy số liệu toàn nhóm + breakdown theo từng nhân viên
        [Fact]
        public async Task L1_DASH_03_ManagerDashboard_AggregatesWholeTeam()
        {
            var s1 = SeedSales();
            var s2 = SeedSales();
            for (var i = 0; i < 5; i++) SeedCompletedOrder(s1.Id, 1_000_000m);
            for (var i = 0; i < 4; i++) SeedCompletedOrder(s2.Id, 1_000_000m);

            var sut = new SalesManagerDashboardService(_db, _kpi);
            var dashboard = await sut.GetDashboardAsync(_from, _to);

            dashboard.TeamKpi.CompletedOrderCount.Should().Be(9, "đủ 9 đơn của cả nhóm");
            dashboard.StaffBreakdown.Should().HaveCount(2);
            dashboard.StaffBreakdown.Single(b => b.SalesStaffId == s1.Id).Kpi.CompletedOrderCount.Should().Be(5);
            dashboard.StaffBreakdown.Single(b => b.SalesStaffId == s2.Id).Kpi.CompletedOrderCount.Should().Be(4);
        }

        // L1-DASH-04 | EP-Valid | CEO thấy số liệu toàn công ty, gồm cả nhóm chỉ số kho và mua hàng
        [Fact]
        public async Task L1_DASH_04_CeoDashboard_CoversCompanyWideGroups()
        {
            var s1 = SeedSales();
            SeedCompletedOrder(s1.Id, 3_000_000m);

            var sut = new CeoDashboardService(_db, _kpi);
            var dashboard = await sut.GetDashboardAsync(_from, _to);

            dashboard.OrgKpi.Should().NotBeNull();
            dashboard.OrgKpi.SalesStaffId.Should().BeNull("phạm vi toàn công ty, không giới hạn theo Sales");
            dashboard.OrgKpi.Revenue.Should().Be(3_000_000m);
            dashboard.Inventory.Should().NotBeNull("CEO phải thấy nhóm chỉ số tồn kho");
            dashboard.PurchaseOrders.Should().NotBeNull("và nhóm chỉ số mua hàng");
            dashboard.Discrepancy.Should().NotBeNull();
        }

        // ── Block: Công thức & khoảng thời gian ─────────────────────────────

        // L1-DASH-06 | BVA | Bộ lọc theo kỳ lấy ĐÚNG cả 2 đơn ở biên đầu/cuối, loại đơn ngoài kỳ
        [Fact]
        public async Task L1_DASH_06_PeriodFilter_IncludesBothBoundaries()
        {
            var s1 = SeedSales();
            var from = DateTime.UtcNow.Date.AddDays(-3);
            var to = DateTime.UtcNow.Date.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);

            SeedCompletedOrder(s1.Id, 1_000_000m, createdAt: from);                    // đúng biên đầu 00:00:00
            SeedCompletedOrder(s1.Id, 1_000_000m, createdAt: to);                      // đúng biên cuối 23:59:59
            SeedCompletedOrder(s1.Id, 9_000_000m, createdAt: from.AddSeconds(-1));     // trước kỳ
            SeedCompletedOrder(s1.Id, 9_000_000m, createdAt: to.AddSeconds(1));        // sau kỳ

            var snapshot = await _kpi.GetSnapshotAsync(s1.Id, from, to);

            snapshot.CompletedOrderCount.Should().Be(2, "cả hai đơn biên đều được tính");
            snapshot.Revenue.Should().Be(2_000_000m, "đơn trước/sau kỳ bị loại");
        }

        // L1-DASH-07 | EP-Valid | Đơn Cancelled KHÔNG được tính vào doanh thu
        [Fact]
        public async Task L1_DASH_07_CancelledOrders_ExcludedFromRevenue()
        {
            var s1 = SeedSales();
            for (var i = 0; i < 3; i++) SeedCompletedOrder(s1.Id, 1_000_000m);

            var (_, profile) = TestData.SeedCustomer(_db);
            _db.Orders.AddRange(
                TestData.Order(profile.Id, o => { o.SalesStaffId = s1.Id; o.OrderStatus = OrderStatus.Cancelled; o.FinalPayment = 5_000_000m; o.CreatedAt = DateTime.UtcNow.AddDays(-1); o.ConfirmedAt = null; }),
                TestData.Order(profile.Id, o => { o.SalesStaffId = s1.Id; o.OrderStatus = OrderStatus.Cancelled; o.FinalPayment = 5_000_000m; o.CreatedAt = DateTime.UtcNow.AddDays(-1); o.ConfirmedAt = null; }));
            _db.SaveChanges();

            var snapshot = await _kpi.GetSnapshotAsync(s1.Id, _from, _to);

            snapshot.CompletedOrderCount.Should().Be(3);
            snapshot.Revenue.Should().Be(3_000_000m, "2 đơn huỷ 5tr không được cộng vào doanh thu");
        }

        // L1-DASH-08 | EP-Valid | Kỳ không có dữ liệu -> mọi chỉ số = 0, không chia cho 0, không ném lỗi
        [Fact]
        public async Task L1_DASH_08_EmptyPeriod_ReturnsZerosWithoutDivideByZero()
        {
            var s1 = SeedSales();
            SeedCompletedOrder(s1.Id, 1_000_000m, createdAt: DateTime.UtcNow.AddDays(-30)); // ngoài kỳ

            var snapshot = await _kpi.GetSnapshotAsync(s1.Id, _from, _to);

            snapshot.Revenue.Should().Be(0m);
            snapshot.CompletedOrderCount.Should().Be(0);
            snapshot.DeliverySuccessRate.Should().Be(0);
            snapshot.ReturningCustomerRate.Should().Be(0);
            snapshot.ProcessingSpeedAvgHours.Should().BeNull("không có đơn nào đã xác nhận trong kỳ");
        }
    }
}
