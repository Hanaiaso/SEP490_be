using FluentAssertions;
using Moq;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.Models;
using VietTien.API.Services.Implementations;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Services
{
    /// <summary>
    /// Sheet: AdminUserService — L1-ADM-01..09. EF InMemory + mock IAuditLogService.
    /// Cả 5 method đều cần bộ 3 tham số audit: actorUserId, actorEmail, ipAddress.
    /// ⚠ Field thật của ChangeUserRoleRequest là NewRole (doc ghi "Role") và bắt buộc có Reason.
    /// </summary>
    public class AdminUserServiceTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly Mock<IAuditLogService> _audit = new();
        private readonly Mock<INotificationService> _notification = new();
        private readonly AdminUserService _sut;

        private static readonly Guid ActorId = Guid.NewGuid();
        private const string ActorEmail = "admin@viettien.com";
        private const string ActorIp = "192.168.1.10";

        public AdminUserServiceTests()
        {
            _sut = new AdminUserService(_db, _audit.Object, _notification.Object);
        }

        private User SeedStaff(SystemRole role = SystemRole.SalesStaff, Action<User>? mutate = null)
        {
            var user = TestData.User(u =>
            {
                u.Role = role;
                u.IsActive = true;
                u.RefreshToken = "rt-existing";
                u.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
                mutate?.Invoke(u);
            });
            _db.Users.Add(user);
            _db.SaveChanges();
            return user;
        }

        // ── Block: CreateStaffAsync() / ChangeRoleAsync() ────────────────────

        // L1-ADM-01 | EP-Valid | Tạo nhân viên -> mật khẩu lưu dạng hash, đúng vai trò, có audit
        [Fact]
        public async Task L1_ADM_01_CreateStaff_HashesPasswordAndAudits()
        {
            var dto = await _sut.CreateStaffAsync(new CreateStaffUserRequest
            {
                FullName = "Trần Thị B",
                Email = "sales01@viettien.com",
                PhoneNumber = "0911222333",
                Password = "P@ssw0rd",
                Role = "SalesStaff"
            }, ActorId, ActorEmail, ActorIp);

            dto.Role.Should().Be("SalesStaff");
            var saved = _db.Users.Single(u => u.Email == "sales01@viettien.com");
            saved.PasswordHash.Should().NotBe("P@ssw0rd");
            BCrypt.Net.BCrypt.Verify("P@ssw0rd", saved.PasswordHash).Should().BeTrue();

            _audit.Verify(a => a.LogAsync("User", saved.Id.ToString(), "CREATE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), ActorIp), Times.Once);
        }

        // L1-ADM-02 | EP-Invalid | Tạo nhân viên trùng email -> từ chối, không tạo user thứ 2
        [Fact]
        public async Task L1_ADM_02_CreateStaff_DuplicateEmail_IsRejected()
        {
            SeedStaff(mutate: u => u.Email = "sales01@viettien.com");

            var act = () => _sut.CreateStaffAsync(new CreateStaffUserRequest
            {
                FullName = "Người trùng email",
                Email = "sales01@viettien.com",
                Password = "P@ssw0rd",
                Role = "SalesStaff"
            }, ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<InvalidOperationException>();
            _db.Users.Count(u => u.Email == "sales01@viettien.com").Should().Be(1);
        }

        // L1-ADM-03 | EP-Valid | Đổi vai trò -> ghi audit before/after và buộc đăng nhập lại
        [Fact]
        public async Task L1_ADM_03_ChangeRole_AuditsBeforeAfter()
        {
            var user = SeedStaff(SystemRole.SalesStaff);

            var dto = await _sut.ChangeRoleAsync(user.Id,
                new ChangeUserRoleRequest { NewRole = "SalesManager", Reason = "Thăng chức quý III" },
                ActorId, ActorEmail, ActorIp);

            dto.Role.Should().Be("SalesManager");
            _db.Users.Single(u => u.Id == user.Id).RefreshToken.Should().BeNull("đổi quyền phải buộc đăng nhập lại");

            _audit.Verify(a => a.LogAsync("User", user.Id.ToString(), "ROLE_CHANGE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), "Thăng chức quý III", ActorIp), Times.Once);
        }

        // L1-ADM-04 | EP-Invalid | Đổi sang vai trò không nằm trong danh sách hợp lệ -> từ chối, giữ vai trò cũ
        [Fact]
        public async Task L1_ADM_04_ChangeRole_UnknownRole_IsRejected()
        {
            var user = SeedStaff(SystemRole.SalesStaff);

            var act = () => _sut.ChangeRoleAsync(user.Id,
                new ChangeUserRoleRequest { NewRole = "SuperUser", Reason = "Thử vai trò lạ" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>();
            _db.Users.Single(u => u.Id == user.Id).Role.Should().Be(SystemRole.SalesStaff);
        }

        // L1-ADM-05 | EP-Invalid | Hạ vai trò Sales đang phụ trách khách -> phải bàn giao trước
        // 🔴 SPEC GAP v2.2: ChangeRoleAsync KHÔNG kiểm tra CustomerProfile.AssignedSalesStaffId.
        // 5 khách sẽ mất người phụ trách trong im lặng -> test ĐỎ cho tới khi bổ sung guard (FT-04 NAC-03).
        [Fact]
        public async Task L1_ADM_05_ChangeRole_SalesWithAssignedCustomers_RequiresHandover()
        {
            var sales = SeedStaff(SystemRole.SalesStaff);
            for (var i = 0; i < 5; i++)
            {
                var (customer, profile) = TestData.SeedCustomer(_db);
                profile.AssignedSalesStaffId = sales.Id;
            }
            _db.SaveChanges();

            var act = () => _sut.ChangeRoleAsync(sales.Id,
                new ChangeUserRoleRequest { NewRole = "WarehouseStaff", Reason = "Điều chuyển sang kho" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("không được để 5 khách mất người phụ trách");
            _db.Users.Single(u => u.Id == sales.Id).Role.Should().Be(SystemRole.SalesStaff);
        }

        // ── Block: SetActiveStatusAsync() / SearchAsync() ────────────────────

        // L1-ADM-06 | EP-Valid | Vô hiệu hoá tài khoản -> refresh token bị thu hồi, có audit
        [Fact]
        public async Task L1_ADM_06_Deactivate_RevokesRefreshTokenAndAudits()
        {
            var user = SeedStaff(SystemRole.SalesStaff);
            SeedStaff(SystemRole.SalesStaff); // còn 1 Sales khác đang hoạt động -> không vướng guard "Sales cuối cùng" (L1-ADM-07)

            var dto = await _sut.SetActiveStatusAsync(user.Id,
                new SetUserActiveStatusRequest { IsActive = false, Reason = "Nghỉ việc" },
                ActorId, ActorEmail, ActorIp);

            dto.IsActive.Should().BeFalse();
            var saved = _db.Users.Single(u => u.Id == user.Id);
            saved.RefreshToken.Should().BeNull("phiên hiện tại không được dùng tiếp");
            saved.RefreshTokenExpiryTime.Should().BeNull();

            _audit.Verify(a => a.LogAsync("User", user.Id.ToString(), "STATUS_CHANGE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), "Nghỉ việc", ActorIp), Times.Once);
        }

        // L1-ADM-07 | EP-Invalid | Vô hiệu hoá Sales duy nhất còn lại -> phải chặn/cảnh báo
        // 🔴 SPEC GAP v2.2: SetActiveStatusAsync KHÔNG kiểm tra còn bao nhiêu Sales đang hoạt động.
        // Khóa người cuối cùng sẽ khiến không còn ai nhận khách mới -> test ĐỎ (FT-04 NAC-03; BV-02).
        [Fact]
        public async Task L1_ADM_07_Deactivate_LastActiveSales_IsBlocked()
        {
            var onlySales = SeedStaff(SystemRole.SalesStaff);

            var act = () => _sut.SetActiveStatusAsync(onlySales.Id,
                new SetUserActiveStatusRequest { IsActive = false, Reason = "Nghỉ việc" },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>("không còn ai trong round-robin để nhận khách mới");
            _db.Users.Single(u => u.Id == onlySales.Id).IsActive.Should().BeTrue();
        }

        // L1-ADM-08 | EP-Valid | Search lọc theo vai trò + trạng thái, trả PagedResultDto đúng TotalCount
        [Fact]
        public async Task L1_ADM_08_Search_FiltersByRoleAndStatus()
        {
            SeedStaff(SystemRole.SalesStaff);
            SeedStaff(SystemRole.SalesStaff);
            SeedStaff(SystemRole.SalesStaff);
            SeedStaff(SystemRole.SalesStaff, u => u.IsActive = false);
            SeedStaff(SystemRole.WarehouseStaff);

            var result = await _sut.SearchAsync(new AdminUserQueryDto { Role = "SalesStaff", IsActive = true });

            result.TotalCount.Should().Be(3);
            result.Items.Should().OnlyContain(i => i.Role == "SalesStaff" && i.IsActive);
        }

        // L1-ADM-09 | EP-Invalid | GetById với id không tồn tại -> not-found, không lộ thông tin
        [Fact]
        public async Task L1_ADM_09_GetById_Unknown_ThrowsNotFound()
        {
            var act = () => _sut.GetByIdAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        // ── Block: RevokeSessionAsync() / ToDto() session mapping (P2-7, UC-55) ──

        // L1-ADM-10 | EP-Valid | User có refresh token hợp lệ -> thu hồi, có audit
        [Fact]
        public async Task L1_ADM_10_RevokeSession_ActiveSession_RevokesAndAudits()
        {
            var user = SeedStaff(SystemRole.SalesStaff); // đã có RefreshToken theo mặc định của SeedStaff

            var dto = await _sut.RevokeSessionAsync(user.Id,
                new RevokeSessionRequest { Reason = "Nghi ngờ tài khoản bị lộ mật khẩu" },
                ActorId, ActorEmail, ActorIp);

            dto.HasActiveSession.Should().BeFalse();
            var saved = _db.Users.Single(u => u.Id == user.Id);
            saved.RefreshToken.Should().BeNull();
            saved.RefreshTokenExpiryTime.Should().BeNull();

            _audit.Verify(a => a.LogAsync("User", user.Id.ToString(), "SESSION_REVOKE",
                ActorId, ActorEmail, It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), "Nghi ngờ tài khoản bị lộ mật khẩu", ActorIp), Times.Once);
        }

        // L1-ADM-11 | EP-Invalid | User không có phiên hoạt động -> no-op, không ghi audit
        [Fact]
        public async Task L1_ADM_11_RevokeSession_NoActiveSession_IsNoOpWithoutAudit()
        {
            var user = SeedStaff(SystemRole.SalesStaff, u =>
            {
                u.RefreshToken = null;
                u.RefreshTokenExpiryTime = null;
            });

            var dto = await _sut.RevokeSessionAsync(user.Id,
                new RevokeSessionRequest { Reason = "Kiểm tra không có phiên" },
                ActorId, ActorEmail, ActorIp);

            dto.HasActiveSession.Should().BeFalse();
            _audit.Verify(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), "SESSION_REVOKE",
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // L1-ADM-12 | EP-Invalid | Thiếu lý do -> từ chối, không đổi gì
        [Fact]
        public async Task L1_ADM_12_RevokeSession_MissingReason_IsRejected()
        {
            var user = SeedStaff(SystemRole.SalesStaff);

            var act = () => _sut.RevokeSessionAsync(user.Id,
                new RevokeSessionRequest { Reason = "  " },
                ActorId, ActorEmail, ActorIp);

            await act.Should().ThrowAsync<Exception>();
            _db.Users.Single(u => u.Id == user.Id).RefreshToken.Should().NotBeNull();
        }

        // L1-ADM-13 | EP-Valid | ToDto tính HasActiveSession theo hạn refresh token (còn hạn/hết hạn)
        [Fact]
        public async Task L1_ADM_13_Search_MapsHasActiveSessionFromExpiry()
        {
            SeedStaff(SystemRole.SalesStaff, u => u.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)); // còn hạn
            SeedStaff(SystemRole.SalesStaff, u => u.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(-1)); // đã hết hạn

            var result = await _sut.SearchAsync(new AdminUserQueryDto { Role = "SalesStaff" });

            result.Items.Should().ContainSingle(i => i.HasActiveSession);
            result.Items.Should().ContainSingle(i => !i.HasActiveSession);
        }
    }
}
