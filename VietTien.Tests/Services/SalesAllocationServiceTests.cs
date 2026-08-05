using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VietTien.API.Data;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: SalesAllocationService — L1-SA-01..05. EF InMemory + mock ILogger/IHubContext&lt;SalesHub&gt;/INotificationService.
    /// Ghi chú L1-SA-02: hành vi thật khi cursor ở staff cuối là gán cho staff ĐẦU danh sách (wrap về S1),
    /// khác câu chữ trong spec ('assigned to S2').
    /// </summary>
    public class SalesAllocationServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly SalesAllocationService _sut;
        private readonly User _s1;
        private readonly User _s2;

        public SalesAllocationServiceTests()
        {
            _sut = new SalesAllocationService(
                _db,
                new Mock<ILogger<SalesAllocationService>>().Object,
                MockHubContext.Create<SalesHub>().Object,
                new Mock<INotificationService>().Object);

            _s1 = TestData.User(u => { u.Role = SystemRole.SalesStaff; u.CreatedAt = DateTime.UtcNow.AddDays(-2); });
            _s2 = TestData.User(u => { u.Role = SystemRole.SalesStaff; u.CreatedAt = DateTime.UtcNow.AddDays(-1); });
            _db.Users.AddRange(_s1, _s2);
            _db.SaveChanges();
        }

        private User SeedUnassignedCustomer()
        {
            var (user, _) = TestData.SeedCustomer(_db);
            return user;
        }

        // L1-SA-01 | EP-Valid | Khách mới, cursor rỗng -> gán cho staff đầu tiên trong pool, cursor cập nhật
        [Fact]
        public async Task L1_SA_01_AutoAssign_NewCustomer_AssignedToNextStaff()
        {
            var customer = SeedUnassignedCustomer();

            await _sut.AutoAssignCustomerAsync(customer.Id);

            var profile = _db.CustomerProfiles.Single(cp => cp.UserId == customer.Id);
            profile.AssignedSalesStaffId.Should().Be(_s1.Id); // cursor null -> bắt đầu từ staff đầu tiên
            profile.AssignmentSource.Should().Be(AssignmentSource.RoundRobin);
            _db.RoundRobinStates.Single().LastAssignedSalesStaffId.Should().Be(_s1.Id);
        }

        // L1-SA-02 | EP-Valid | Cursor ở staff CUỐI -> lần gán tiếp theo wrap về staff ĐẦU danh sách
        [Fact]
        public async Task L1_SA_02_AutoAssign_CursorAtLastStaff_WrapsToFirst()
        {
            _db.RoundRobinStates.Add(new RoundRobinState { LastAssignedSalesStaffId = _s2.Id });
            _db.SaveChanges();
            var customer = SeedUnassignedCustomer();

            await _sut.AutoAssignCustomerAsync(customer.Id);

            var profile = _db.CustomerProfiles.Single(cp => cp.UserId == customer.Id);
            profile.AssignedSalesStaffId.Should().Be(_s1.Id); // wrap từ S2 (cuối) về S1 (đầu)
            _db.RoundRobinStates.Single().LastAssignedSalesStaffId.Should().Be(_s1.Id);
        }

        // L1-SA-03 | EP-Valid | Bulk-assign phân bổ HẾT khách chưa gán theo round-robin, báo đúng số lượng
        [Fact]
        public async Task L1_SA_03_AssignUnassigned_AllDistributed()
        {
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            SeedUnassignedCustomer();
            SeedUnassignedCustomer();
            SeedUnassignedCustomer();

            var result = await _sut.AssignUnassignedCustomersAsync(manager.Id);

            result.AssignedCount.Should().Be(3);
            _db.CustomerProfiles.Count(cp => cp.AssignedSalesStaffId == null).Should().Be(0);
            // Round-robin: 3 khách chia cho 2 staff -> 2 người đều có khách
            _db.CustomerProfiles.Count(cp => cp.AssignedSalesStaffId == _s1.Id).Should().Be(2);
            _db.CustomerProfiles.Count(cp => cp.AssignedSalesStaffId == _s2.Id).Should().Be(1);
        }

        // L1-SA-04 | EP-Invalid | Staff mở chi tiết khách KHÔNG thuộc quyền phụ trách -> từ chối, không lộ dữ liệu
        [Fact]
        public async Task L1_SA_04_GetMyCustomerDetail_NotMyCustomer_Forbidden()
        {
            var (_, profile9) = TestData.SeedCustomer(_db);
            profile9.AssignedSalesStaffId = _s2.Id; // khách của S2
            _db.SaveChanges();

            var act = () => _sut.GetMyCustomerDetailAsync(_s1.Id, profile9.Id);

            await act.Should().ThrowAsync<Exception>()
                .WithMessage("Không tìm thấy khách hàng hoặc khách không thuộc quyền phụ trách của bạn.");
        }

        // L1-SA-05 | EP-Valid | Cập nhật note đúng khách của mình, khách khác không bị ảnh hưởng
        [Fact]
        public async Task L1_SA_05_UpdateCustomerNote_OnlyTargetCustomerChanged()
        {
            var (_, c1) = TestData.SeedCustomer(_db);
            var (_, c2) = TestData.SeedCustomer(_db);
            c1.AssignedSalesStaffId = _s1.Id;
            c2.AssignedSalesStaffId = _s1.Id;
            c2.SalesNote = "note cũ";
            _db.SaveChanges();

            await _sut.UpdateCustomerNoteAsync(_s1.Id, c1.Id, "VIP");

            _db.CustomerProfiles.Single(cp => cp.Id == c1.Id).SalesNote.Should().Be("VIP");
            _db.CustomerProfiles.Single(cp => cp.Id == c2.Id).SalesNote.Should().Be("note cũ");
        }

        // ── Block: ⊕ v2.1 — Referral & cấu hình round-robin ─────────────────
        //
        // ⚠ Khác doc: ResolveReferralStaffAsync trả TUPLE (Guid? StaffId, string? Error) — KHÔNG ném
        //   exception, nên các case "từ chối" phải assert trên Error thay vì ThrowAsync.
        // ⚠ EnsureCustomerProfileAsync(userId, taxCode) nhận 2 THAM SỐ (doc ghi 1).
        // ⚠ ISalesAllocationService VẪN CÒN AutoAssignCustomerAsync / AssignUnassignedCustomersAsync —
        //   ghi chú "no longer on interface" ở SA-01..03 của doc đã lỗi thời.

        // L1-SA-06 | EP-Valid | Mã giới thiệu hợp lệ, duy nhất -> trả đúng Sales được giới thiệu
        [Fact]
        public async Task L1_SA_06_ResolveReferral_ValidUniqueCode_ReturnsStaff()
        {
            _s1.ReferralCode = "S1-REF";
            _db.SaveChanges();

            var (staffId, error) = await _sut.ResolveReferralStaffAsync("S1-REF");

            staffId.Should().Be(_s1.Id);
            error.Should().BeNull();
        }

        // L1-SA-07 | EP-Invalid | Mã giới thiệu không tồn tại -> KHÔNG chọn bừa một Sales bất kỳ
        [Fact]
        public async Task L1_SA_07_ResolveReferral_UnknownCode_ReturnsErrorNotRandomStaff()
        {
            _s1.ReferralCode = "S1-REF";
            _db.SaveChanges();

            var (staffId, error) = await _sut.ResolveReferralStaffAsync("XXX");

            staffId.Should().BeNull("không được gán bừa cho một Sales bất kỳ");
            error.Should().NotBeNullOrWhiteSpace();
        }

        // L1-SA-08 | EP-Invalid | Mã giới thiệu thuộc Sales ĐÃ NGỪNG HOẠT ĐỘNG -> không gán
        // 🔴 SPEC GAP v2.2: ResolveReferralStaffAsync chỉ lọc theo Role == SalesStaff, KHÔNG xét
        // User.IsActive -> khách vẫn bị gán cho nhân viên đã nghỉ. Test ĐỎ cho tới khi thêm điều kiện IsActive.
        [Fact]
        public async Task L1_SA_08_ResolveReferral_InactiveStaff_IsNotAssigned()
        {
            var s3 = TestData.User(u => { u.Role = SystemRole.SalesStaff; u.IsActive = false; u.ReferralCode = "S3-REF"; });
            _db.Users.Add(s3);
            _db.SaveChanges();

            var (staffId, error) = await _sut.ResolveReferralStaffAsync("S3-REF");

            staffId.Should().BeNull("Sales đã ngừng hoạt động không được nhận khách mới");
            error.Should().NotBeNullOrWhiteSpace();
        }

        // L1-SA-09 | EP-Invalid | Mã giới thiệu khớp NHIỀU Sales -> báo mơ hồ, KHÔNG tự chọn
        [Fact]
        public async Task L1_SA_09_ResolveReferral_AmbiguousCode_IsRejected()
        {
            _s1.ReferralCode = "REF-CHUNG";
            _s2.ReferralCode = "REF-CHUNG";
            _db.SaveChanges();

            var (staffId, error) = await _sut.ResolveReferralStaffAsync("REF-CHUNG");

            staffId.Should().BeNull("không được tự chọn 1 trong 2");
            error.Should().Contain("nhiều nhân viên");
        }

        // L1-SA-10 | EP-Valid | EnsureCustomerProfile gọi 2 lần -> chỉ tồn tại ĐÚNG 1 CustomerProfile
        [Fact]
        public async Task L1_SA_10_EnsureCustomerProfile_IsIdempotent()
        {
            var user = TestData.User();
            _db.Users.Add(user);
            _db.SaveChanges();

            await _sut.EnsureCustomerProfileAsync(user.Id, "0101234567");
            await _sut.EnsureCustomerProfileAsync(user.Id, "0101234567");

            _db.CustomerProfiles.Count(cp => cp.UserId == user.Id).Should().Be(1);
            _db.CustomerProfiles.Single(cp => cp.UserId == user.Id).TaxCode.Should().Be("0101234567");
        }

        // L1-SA-11 | EP-Valid | Tắt 1 staff khỏi round-robin -> lần gán kế tiếp không rơi vào staff đó
        [Fact]
        public async Task L1_SA_11_UpdateRoundRobin_DeactivatedStaffIsSkipped()
        {
            var s3 = TestData.User(u => { u.Role = SystemRole.SalesStaff; u.CreatedAt = DateTime.UtcNow; });
            _db.Users.Add(s3);
            _db.SaveChanges();
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();

            var state = await _sut.UpdateRoundRobinAsync(manager.Id, new API.DTOs.Sales.UpdateRoundRobinRequest
            {
                Participants = new List<API.DTOs.Sales.ParticipantToggleDto>
                {
                    new() { StaffId = _s2.Id, IsActive = false } // loại S2 khỏi vòng phân công
                }
            });

            state.Participants.Single(p => p.StaffId == _s2.Id).IsActive.Should().BeFalse();

            // Gán 3 khách liên tiếp — không khách nào được rơi vào S2
            for (var i = 0; i < 3; i++)
                await _sut.AutoAssignCustomerAsync(SeedUnassignedCustomer().Id);

            _db.CustomerProfiles.Any(cp => cp.AssignedSalesStaffId == _s2.Id)
                .Should().BeFalse("S2 đã bị loại khỏi round-robin");
        }

        // L1-SA-12 | EP-Invalid | Cập nhật với danh sách participant RỖNG -> cấu hình cũ giữ nguyên
        // ⚠ Khác doc: service BỎ QUA (no-op) chứ không ném lỗi rõ ràng — kết quả bảo vệ vẫn đạt
        //   (không rơi vào trạng thái "không còn ai để phân công"). Xem DOC_MISMATCHES.md.
        [Fact]
        public async Task L1_SA_12_UpdateRoundRobin_EmptyParticipants_KeepsExistingConfig()
        {
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();
            var before = await _sut.GetRoundRobinStateAsync();

            var after = await _sut.UpdateRoundRobinAsync(manager.Id, new API.DTOs.Sales.UpdateRoundRobinRequest
            {
                Participants = new List<API.DTOs.Sales.ParticipantToggleDto>()
            });

            after.Participants.Count(p => p.IsActive)
                .Should().Be(before.Participants.Count(p => p.IsActive))
                .And.BeGreaterThan(0, "phải luôn còn người để phân công");
        }

        // L1-SA-13 | EP-Valid | GetRoundRobinState trả đúng con trỏ và danh sách hiện hành
        [Fact]
        public async Task L1_SA_13_GetRoundRobinState_ReflectsCursorAndParticipants()
        {
            var manager = TestData.User(u => u.Role = SystemRole.SalesManager);
            _db.Users.Add(manager);
            _db.SaveChanges();
            await _sut.GetRoundRobinStateAsync(); // seed participants

            await _sut.UpdateRoundRobinAsync(manager.Id, new API.DTOs.Sales.UpdateRoundRobinRequest
            {
                CursorStaffId = _s2.Id
            });

            var state = await _sut.GetRoundRobinStateAsync();

            state.CursorStaffId.Should().Be(_s2.Id);
            state.Participants.Select(p => p.StaffId).Should().Contain(new[] { _s1.Id, _s2.Id });
        }
    }
}
