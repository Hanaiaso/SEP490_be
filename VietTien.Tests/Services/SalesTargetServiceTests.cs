using FluentAssertions;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: SalesTargetService — Sales Manager đặt/xem mục tiêu doanh thu tháng cho từng Sales Staff.
    /// EF InMemory, cùng convention với AdminUserServiceTests.
    /// </summary>
    public class SalesTargetServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly SalesTargetService _sut;
        private readonly User _manager;
        private readonly User _staff1;
        private readonly User _staff2;

        public SalesTargetServiceTests()
        {
            _sut = new SalesTargetService(_db);
            _manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _staff1 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _staff2 = TestData.User(u => u.Role = SystemRole.SalesStaff);
            _db.Users.AddRange(_manager, _staff1, _staff2);
            _db.SaveChanges();
        }

        private Order SeedCompletedOrder(Guid salesStaffId, decimal finalPayment, decimal amountPaid, DateTime createdAt)
        {
            var (_, profile) = TestData.SeedCustomer(_db);
            var order = TestData.Order(profile.Id, o =>
            {
                o.SalesStaffId = salesStaffId;
                o.OrderStatus = OrderStatus.Completed;
                o.FinalPayment = finalPayment;
                o.AmountPaid = amountPaid;
                o.CreatedAt = createdAt;
            });
            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        // L1-TGT-01 | EP-Valid | Đặt mục tiêu lần đầu -> tạo dòng mới, ghi đúng người đặt
        [Fact]
        public async Task L1_TGT_01_SetTarget_FirstTime_CreatesRow()
        {
            var now = DateTime.UtcNow;

            var result = await _sut.SetTargetAsync(new SetSalesTargetRequest
            {
                SalesStaffId = _staff1.Id,
                Year = now.Year,
                Month = now.Month,
                TargetAmount = 50_000_000m,
            }, _manager.Id);

            result.TargetAmount.Should().Be(50_000_000m);
            result.SetByName.Should().Be(_manager.FullName);
            _db.SalesRevenueTargets.Should().ContainSingle(t => t.SalesStaffId == _staff1.Id && t.TargetAmount == 50_000_000m);
        }

        // L1-TGT-02 | EP-Valid | Đặt lại mục tiêu tháng đã có -> UPDATE, không tạo dòng thứ 2
        [Fact]
        public async Task L1_TGT_02_SetTarget_SameMonthTwice_UpdatesNotDuplicates()
        {
            var now = DateTime.UtcNow;
            await _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _staff1.Id, Year = now.Year, Month = now.Month, TargetAmount = 50_000_000m }, _manager.Id);

            await _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _staff1.Id, Year = now.Year, Month = now.Month, TargetAmount = 80_000_000m }, _manager.Id);

            _db.SalesRevenueTargets.Should().ContainSingle(t => t.SalesStaffId == _staff1.Id)
                .Which.TargetAmount.Should().Be(80_000_000m);
        }

        // L1-TGT-03 | EP-Invalid | Đặt mục tiêu cho tài khoản KHÔNG phải Sales Staff -> từ chối
        [Fact]
        public async Task L1_TGT_03_SetTarget_NonSalesStaffAccount_Rejected()
        {
            var now = DateTime.UtcNow;

            var act = () => _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _manager.Id, Year = now.Year, Month = now.Month, TargetAmount = 10_000_000m }, _manager.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.SalesRevenueTargets.Should().BeEmpty();
        }

        // L1-TGT-04 | EP-Invalid | Nhân viên không tồn tại -> KeyNotFoundException
        [Fact]
        public async Task L1_TGT_04_SetTarget_UnknownStaff_ThrowsNotFound()
        {
            var now = DateTime.UtcNow;

            var act = () => _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = Guid.NewGuid(), Year = now.Year, Month = now.Month, TargetAmount = 10_000_000m }, _manager.Id);

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        // L1-TGT-05 | EP-Valid | Danh sách toàn đội: nhân viên chưa có mục tiêu -> TargetAmount=0, AchievementRate=null
        [Fact]
        public async Task L1_TGT_05_GetTeamTargets_StaffWithoutTarget_ReturnsZeroAndNullRate()
        {
            var now = DateTime.UtcNow;
            SeedCompletedOrder(_staff1.Id, 1_000_000m, 1_000_000m, now); // staff1 có doanh thu nhưng chưa có mục tiêu

            var result = await _sut.GetTeamTargetsAsync(now.Year, now.Month);

            var staff1Row = result.Should().ContainSingle(r => r.SalesStaffId == _staff1.Id).Subject;
            staff1Row.TargetAmount.Should().Be(0);
            staff1Row.ActualRevenue.Should().Be(1_000_000m, "vẫn phải hiện doanh thu thực tế dù chưa có mục tiêu");
            staff1Row.AchievementRate.Should().BeNull();
        }

        // L1-TGT-06 | EP-Valid | Danh sách toàn đội: đúng doanh thu THÁNG ĐÓ (AmountPaid, không lẫn tháng khác/nhân viên khác)
        [Fact]
        public async Task L1_TGT_06_GetTeamTargets_RevenueScopedToMonthAndStaff()
        {
            var now = DateTime.UtcNow;
            var thisMonthStart = new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc);

            await _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _staff1.Id, Year = now.Year, Month = now.Month, TargetAmount = 2_000_000m }, _manager.Id);

            SeedCompletedOrder(_staff1.Id, 1_000_000m, 1_000_000m, thisMonthStart); // trong tháng, tính
            SeedCompletedOrder(_staff1.Id, 1_000_000m, 500_000m, thisMonthStart);   // trong tháng, trả thiếu -> chỉ tính AmountPaid
            SeedCompletedOrder(_staff1.Id, 9_000_000m, 9_000_000m, thisMonthStart.AddMonths(-1)); // tháng trước -> KHÔNG tính
            SeedCompletedOrder(_staff2.Id, 5_000_000m, 5_000_000m, thisMonthStart); // nhân viên khác -> KHÔNG tính vào staff1

            var result = await _sut.GetTeamTargetsAsync(now.Year, now.Month);

            var staff1Row = result.Should().ContainSingle(r => r.SalesStaffId == _staff1.Id).Subject;
            staff1Row.ActualRevenue.Should().Be(1_500_000m);
            staff1Row.AchievementRate.Should().BeApproximately(0.75, 0.001);
        }

        // L1-TGT-07 | EP-Valid | GetCurrentMonthTargetAndRevenueAsync(null) -> cộng dồn mục tiêu + doanh thu TOÀN ĐỘI
        [Fact]
        public async Task L1_TGT_07_GetCurrentMonthTeamTotal_SumsAcrossAllStaff()
        {
            var now = DateTime.UtcNow;
            var thisMonth = new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc);

            await _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _staff1.Id, Year = now.Year, Month = now.Month, TargetAmount = 3_000_000m }, _manager.Id);
            await _sut.SetTargetAsync(new SetSalesTargetRequest
            { SalesStaffId = _staff2.Id, Year = now.Year, Month = now.Month, TargetAmount = 7_000_000m }, _manager.Id);
            SeedCompletedOrder(_staff1.Id, 1_000_000m, 1_000_000m, thisMonth);
            SeedCompletedOrder(_staff2.Id, 2_000_000m, 2_000_000m, thisMonth);

            var (target, revenue) = await _sut.GetCurrentMonthTargetAndRevenueAsync(null);

            target.Should().Be(10_000_000m);
            revenue.Should().Be(3_000_000m);
        }
    }
}
