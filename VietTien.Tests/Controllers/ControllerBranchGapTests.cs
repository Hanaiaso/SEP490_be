using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Customer;
using VietTien.API.DTOs.Product;
using VietTien.API.DTOs.UserProfile;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>
    /// Case nhắm riêng vào các NHÁNH chưa được phủ, không phải dòng chưa được phủ.
    ///
    /// Vì sao cần một nhóm riêng: nhiều controller đã đạt line coverage 95–100% mà branch coverage
    /// chỉ 58–72%. Nguyên nhân là ba khuôn lặp lại trong dự án, mỗi khuôn có một hướng rẽ mà bộ test
    /// hiện tại **về mặt logic không thể chạm tới**:
    ///
    ///   1. `if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out var id))`
    ///      Test "không có claim" làm `IsNullOrEmpty` trả true, toán tử `||` đoản mạch nên
    ///      `Guid.TryParse` KHÔNG BAO GIỜ chạy. Phải có claim rác mới chạm được vế thứ hai.
    ///
    ///   2. `User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty` và
    ///      `HttpContext.Connection.RemoteIpAddress?.ToString()`
    ///      Test chưa từng gắn claim Email hay IP nên chỉ nhánh null được chạy.
    ///
    ///   3. Ternary kẹp biên (`page &lt; 1 ? 1 : page`) và `if (list.Any())`
    ///      Chỉ gọi với tham số mặc định / dữ liệu khác rỗng thì một vế luôn bị bỏ.
    ///
    /// Mỗi case dưới đây vẫn assert một hành vi thật, không phải test rỗng chạy cho có.
    /// </summary>
    public class NotificationsControllerBranchGapTests
    {
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly NotificationsController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public NotificationsControllerBranchGapTests()
            => _sut = new NotificationsController(_db).WithUser(_userId, "Customer");

        private async Task SeedAsync(int unread, int read)
        {
            for (var i = 0; i < unread; i++)
                _db.Notifications.Add(new Notification { RecipientUserId = _userId, Title = $"chua doc {i}", IsRead = false });
            for (var i = 0; i < read; i++)
                _db.Notifications.Add(new Notification { RecipientUserId = _userId, Title = $"da doc {i}", IsRead = true });
            await _db.SaveChangesAsync();
        }

        // ── khuôn 1: claim rác ───────────────────────────────────────────────

        [Fact]
        public async Task GetNotifications_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.GetNotifications()).StatusOf().Should().Be(401,
                "token mang id không phải Guid phải bị từ chối như không có token");
        }

        [Fact]
        public async Task GetUnreadCount_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.GetUnreadCount()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MarkAsRead_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.MarkAsRead(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MarkAllAsRead_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.MarkAllAsRead()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteNotification_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.DeleteNotification(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteAllRead_WithMalformedUserId_Returns401()
        {
            _sut.WithMalformedUserId();

            (await _sut.DeleteAllRead()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MarkAsRead_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.MarkAsRead(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task MarkAllAsRead_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.MarkAllAsRead()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteNotification_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.DeleteNotification(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteAllRead_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.DeleteAllRead()).StatusOf().Should().Be(401);
        }

        // ── khuôn 3: biên phân trang ─────────────────────────────────────────

        [Fact]
        public async Task GetNotifications_WhenPageBelowOne_ClampsToFirstPage()
        {
            await SeedAsync(unread: 3, read: 0);

            var body = ((await _sut.GetNotifications(pageNumber: 0)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("PageNumber")!.GetValue(body).Should().Be(1,
                "page < 1 phải kẹp về 1 chứ không được tính Skip âm");
        }

        [Fact]
        public async Task GetNotifications_WhenLimitBelowOne_FallsBackTo20()
        {
            await SeedAsync(unread: 1, read: 0);

            var body = ((await _sut.GetNotifications(pageSize: 0)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("PageSize")!.GetValue(body).Should().Be(20);
        }

        [Fact]
        public async Task GetNotifications_WhenLimitAboveMax_CapsAt100()
        {
            await SeedAsync(unread: 1, read: 0);

            var body = ((await _sut.GetNotifications(pageSize: 5000)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("PageSize")!.GetValue(body).Should().Be(100,
                "chặn trần để một request không kéo về toàn bộ bảng thông báo");
        }

        [Fact]
        public async Task GetNotifications_SecondPage_SkipsFirstPageItems()
        {
            await SeedAsync(unread: 5, read: 0);

            var body = ((await _sut.GetNotifications(pageNumber: 2, pageSize: 2)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("Total")!.GetValue(body).Should().Be(5);
            ((System.Collections.ICollection)body.GetType().GetProperty("Items")!.GetValue(body)!)
                .Count.Should().Be(2, "trang 2 với limit 2 phải trả đúng 2 bản ghi");
        }

        // P1: filter isRead trước đây bị BE bỏ qua hoàn toàn (chưa từng đọc tham số) khiến tab
        // "Chưa đọc" trên chuông thông báo hiển thị y hệt tab "Tất cả".
        [Fact]
        public async Task GetNotifications_FilterByIsReadFalse_OnlyReturnsUnread()
        {
            await SeedAsync(unread: 2, read: 3);

            var body = ((await _sut.GetNotifications(isRead: false)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("Total")!.GetValue(body).Should().Be(2,
                "isRead=false phải lọc chỉ còn thông báo chưa đọc, không phải toàn bộ danh sách");
            var items = (System.Collections.IEnumerable)body.GetType().GetProperty("Items")!.GetValue(body)!;
            foreach (Notification n in items)
                n.IsRead.Should().BeFalse();
        }

        [Fact]
        public async Task GetNotifications_FilterByIsReadTrue_OnlyReturnsRead()
        {
            await SeedAsync(unread: 2, read: 3);

            var body = ((await _sut.GetNotifications(isRead: true)) as ObjectResult)!.Value!;

            body.GetType().GetProperty("Total")!.GetValue(body).Should().Be(3);
        }

        [Fact]
        public async Task GetNotifications_NoIsReadFilter_ReturnsAll()
        {
            await SeedAsync(unread: 2, read: 3);

            var body = ((await _sut.GetNotifications()) as ObjectResult)!.Value!;

            body.GetType().GetProperty("Total")!.GetValue(body).Should().Be(5,
                "không truyền isRead thì phải trả về toàn bộ, không lọc gì cả");
        }

        // ── khuôn 3: danh sách rỗng ──────────────────────────────────────────

        [Fact]
        public async Task MarkAllAsRead_WhenNothingUnread_StillReturnsOkWithoutSaving()
        {
            await SeedAsync(unread: 0, read: 2);

            (await _sut.MarkAllAsRead()).StatusOf().Should().Be(200,
                "không có gì để đánh dấu vẫn là thành công, không phải lỗi");
        }

        [Fact]
        public async Task DeleteAllRead_WhenNothingRead_ReturnsZeroDeleted()
        {
            await SeedAsync(unread: 2, read: 0);

            var body = ((await _sut.DeleteAllRead()) as ObjectResult)!.Value!;

            body.GetType().GetProperty("Deleted")!.GetValue(body).Should().Be(0);
            _db.Notifications.Count().Should().Be(2, "không được xoá nhầm thông báo chưa đọc");
        }
    }

    /// <summary>Nhánh còn thiếu của VehiclesController (line 93,8% nhưng branch chỉ 58,3%).</summary>
    public class VehiclesControllerBranchGapTests
    {
        private readonly Mock<IVehicleService> _service = new();
        private readonly VehiclesController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public VehiclesControllerBranchGapTests()
            => _sut = new VehiclesController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task Create_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            // GetUserId() ném UnauthorizedAccessException, action chỉ có catch(Exception) -> 400.
            (await _sut.Create(new CreateVehicleRequest())).StatusOf().Should().Be(400);
            _service.Verify(s => s.CreateAsync(It.IsAny<CreateVehicleRequest>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task Update_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.Update(Guid.NewGuid(), new UpdateVehicleRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            (await _sut.Create(new CreateVehicleRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_ForwardsActorEmailAndIpToAuditTrail()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateVehicleRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new VehicleDto());
            _sut.WithEmailClaim("admin@viettien.vn").WithRemoteIp("203.0.113.10");

            (await _sut.Create(new CreateVehicleRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.CreateAsync(It.IsAny<CreateVehicleRequest>(), _adminId,
                "admin@viettien.vn", "203.0.113.10"), Times.Once,
                "audit log phải ghi được cả email lẫn IP thật của người thao tác");
        }

        [Fact]
        public async Task Update_WhenNoEmailClaimAndNoIp_PassesEmptyEmailAndNullIp()
        {
            _service.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateVehicleRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new VehicleDto());

            (await _sut.Update(Guid.NewGuid(), new UpdateVehicleRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateVehicleRequest>(),
                _adminId, string.Empty, null), Times.Once,
                "thiếu email/IP thì truyền giá trị rỗng chứ không được nổ NullReference");
        }
    }

    /// <summary>Nhánh còn thiếu của DiscountTiersController (line 93,4% nhưng branch chỉ 58,3%).</summary>
    public class DiscountTiersControllerBranchGapTests
    {
        private readonly Mock<IDiscountTierService> _service = new();
        private readonly DiscountTiersController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public DiscountTiersControllerBranchGapTests()
            => _sut = new DiscountTiersController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task Create_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.Create(new CreateDiscountTierRequest())).StatusOf().Should().Be(400);
            _service.Verify(s => s.CreateAsync(It.IsAny<CreateDiscountTierRequest>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task Update_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.Update(Guid.NewGuid(), new UpdateDiscountTierRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            (await _sut.Create(new CreateDiscountTierRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_ForwardsActorEmailAndIpToAuditTrail()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<CreateDiscountTierRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new DiscountTierDto());
            _sut.WithEmailClaim("ceo@viettien.vn").WithRemoteIp("198.51.100.7");

            (await _sut.Create(new CreateDiscountTierRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.CreateAsync(It.IsAny<CreateDiscountTierRequest>(), _adminId,
                "ceo@viettien.vn", "198.51.100.7"), Times.Once,
                "đổi bậc chiết khấu là hành vi nhạy cảm — audit log phải đủ email và IP");
        }
    }

    /// <summary>Nhánh còn thiếu của AdminUsersController (line 98,5% nhưng branch chỉ 58,3%).</summary>
    public class AdminUsersControllerBranchGapTests
    {
        private readonly Mock<IAdminUserService> _service = new();
        private readonly AdminUsersController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public AdminUsersControllerBranchGapTests()
            => _sut = new AdminUsersController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task Create_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(400);
            _service.Verify(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task ChangeRole_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.ChangeRole(Guid.NewGuid(), new ChangeUserRoleRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_WithoutUserClaim_Returns400()
        {
            _sut.WithAnonymousUser();

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Create_ForwardsActorEmailAndIpToAuditTrail()
        {
            _service.Setup(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), It.IsAny<Guid>(),
                    It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new AdminUserDto());
            _sut.WithEmailClaim("admin@viettien.vn").WithRemoteIp("192.0.2.44");

            (await _sut.Create(new CreateStaffUserRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.CreateStaffAsync(It.IsAny<CreateStaffUserRequest>(), _adminId,
                "admin@viettien.vn", "192.0.2.44"), Times.Once,
                "tạo tài khoản nhân viên phải truy vết được ai làm, từ IP nào");
        }

        [Fact]
        public async Task SetStatus_ForwardsActorEmailAndIp()
        {
            _service.Setup(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new AdminUserDto());
            _sut.WithEmailClaim("admin@viettien.vn").WithRemoteIp("192.0.2.44");

            (await _sut.SetStatus(Guid.NewGuid(), new SetUserActiveStatusRequest())).StatusOf().Should().Be(200);

            _service.Verify(s => s.SetActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<SetUserActiveStatusRequest>(),
                _adminId, "admin@viettien.vn", "192.0.2.44"), Times.Once);
        }
    }

    /// <summary>Nhánh còn thiếu của AdminSystemConfigController (line 100% nhưng branch 83,3%).</summary>
    public class AdminSystemConfigControllerBranchGapTests
    {
        private readonly Mock<ISystemConfigService> _service = new();
        private readonly AdminSystemConfigController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public AdminSystemConfigControllerBranchGapTests()
            => _sut = new AdminSystemConfigController(_service.Object).WithUser(_adminId, "Admin");

        [Fact]
        public async Task Update_WithMalformedUserId_Returns400()
        {
            _sut.WithMalformedUserId();

            (await _sut.Update("SePayThresholdMinutes", new UpdateSystemConfigRequest()))
                .StatusOf().Should().Be(400);
            _service.Verify(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task Update_ForwardsActorEmailAndIpToAuditTrail()
        {
            _service.Setup(s => s.SetValueAsync(It.IsAny<string>(), It.IsAny<UpdateSystemConfigRequest>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(new SystemConfigDto());
            _sut.WithEmailClaim("admin@viettien.vn").WithRemoteIp("192.0.2.99");

            (await _sut.Update("SePayThresholdMinutes", new UpdateSystemConfigRequest()))
                .StatusOf().Should().Be(200);

            _service.Verify(s => s.SetValueAsync("SePayThresholdMinutes", It.IsAny<UpdateSystemConfigRequest>(),
                _adminId, "admin@viettien.vn", "192.0.2.99", It.IsAny<string?>()), Times.Once,
                "đổi cấu hình hệ thống phải truy vết được email + IP");
        }
    }

    /// <summary>Nhánh còn thiếu của UserProfileController / CustomerProfileController (branch 81,8%).</summary>
    public class ProfileControllersBranchGapTests
    {
        private readonly Mock<IUserProfileService> _profile = new();
        private readonly Mock<IAddressService> _address = new();
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IUserRepository> _users = new();
        private readonly Mock<IAddressRepository> _addresses = new();
        private readonly ApplicationDbContext _db = TestDbFactory.Create();

        public ProfileControllersBranchGapTests()
        {
            _uow.SetupGet(u => u.Users).Returns(_users.Object);
            _uow.SetupGet(u => u.Addresses).Returns(_addresses.Object);
        }

        private UserProfileController BuildUserProfile()
            => new UserProfileController(_profile.Object, _address.Object).WithMalformedUserId();

        private CustomerProfileController BuildCustomerProfile()
            => new CustomerProfileController(_uow.Object, _db).WithMalformedUserId();

        [Fact]
        public async Task UserProfile_GetProfile_WithMalformedUserId_Returns401()
        {
            (await BuildUserProfile().GetProfile()).StatusOf().Should().Be(401);
            _profile.Verify(s => s.GetProfileAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task UserProfile_GetAddresses_WithMalformedUserId_Returns401()
        {
            (await BuildUserProfile().GetAddresses()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task UserProfile_ChangePassword_WithMalformedUserId_Returns401()
        {
            (await BuildUserProfile().ChangePassword(new ChangePasswordDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task CustomerProfile_GetProfile_WithMalformedUserId_Returns401()
        {
            (await BuildCustomerProfile().GetProfile()).StatusOf().Should().Be(401);
            _users.Verify(r => r.GetCustomerProfileByUserIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task CustomerProfile_UpdateProfile_WithMalformedUserId_Returns401()
        {
            (await BuildCustomerProfile().UpdateProfile(new CustomerProfileDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task CustomerProfile_GetCreditHistory_WithMalformedUserId_Returns401()
        {
            (await BuildCustomerProfile().GetCreditHistory()).StatusOf().Should().Be(401);
        }

        // ── nhánh fallback `?? User.FindFirst("sub")` ────────────────────────
        // Đây là đường mà token do Google phát hành đi qua: nó dùng claim chuẩn `sub` chứ không
        // dùng `ClaimTypes.NameIdentifier`. Mọi test trước đây đều gắn NameIdentifier nên vế phải
        // của `??` chưa từng chạy — không phải nhánh chết mà là luồng đăng nhập Google thật.

        [Fact]
        public async Task UserProfile_GetProfile_AcceptsSubClaimWhenNameIdentifierAbsent()
        {
            var userId = Guid.NewGuid();
            _profile.Setup(s => s.GetProfileAsync(userId)).ReturnsAsync(new UserProfileDto());
            var sut = new UserProfileController(_profile.Object, _address.Object)
                .WithSubClaimOnly(userId);

            (await sut.GetProfile()).StatusOf().Should().Be(200);
            _profile.Verify(s => s.GetProfileAsync(userId), Times.Once,
                "token Google chỉ có claim `sub` — vẫn phải nhận diện đúng người dùng");
        }

        [Fact]
        public async Task UserProfile_GetAddresses_AcceptsSubClaimWhenNameIdentifierAbsent()
        {
            var userId = Guid.NewGuid();
            _address.Setup(s => s.GetAddressesAsync(userId)).ReturnsAsync(new List<AddressDto>());
            var sut = new UserProfileController(_profile.Object, _address.Object)
                .WithSubClaimOnly(userId);

            (await sut.GetAddresses()).StatusOf().Should().Be(200);
            _address.Verify(s => s.GetAddressesAsync(userId), Times.Once);
        }

        [Fact]
        public async Task CustomerProfile_GetProfile_AcceptsSubClaimWhenNameIdentifierAbsent()
        {
            var userId = Guid.NewGuid();
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(userId))
                .ReturnsAsync(new CustomerProfile { UserId = userId, CompanyName = "Cty Google" });
            var sut = new CustomerProfileController(_uow.Object, _db).WithSubClaimOnly(userId);

            var result = await sut.GetProfile();

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value.Should().BeOfType<CustomerProfileDto>()
                .Which.CompanyName.Should().Be("Cty Google");
        }
    }

    /// <summary>Nhánh còn thiếu của StockTransferController.Create và GoodsIssueController.</summary>
    public class WarehouseControllersBranchGapTests
    {
        [Fact]
        public async Task StockTransfer_Create_WithMalformedUserId_Returns401()
        {
            var service = new Mock<IStockTransferService>();
            var sut = new StockTransferController(service.Object).WithMalformedUserId();

            (await sut.Create(new CreateStockTransferDto())).StatusOf().Should().Be(401,
                "action này kiểm tra claim tường minh nên trả 401 chứ không phải 400");
            service.Verify(s => s.CreateAsync(It.IsAny<CreateStockTransferDto>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GoodsIssue_CreateGoodsIssue_WithMalformedUserId_Returns400()
        {
            var service = new Mock<IGoodsIssueService>();
            var sut = new GoodsIssueController(service.Object).WithMalformedUserId();

            // Ở đây guard chỉ kiểm tra rỗng rồi gọi thẳng `Guid.Parse` -> ném FormatException
            // -> rơi vào catch(Exception) = 400. Khác StockTransferController vốn dùng TryParse.
            (await sut.CreateGoodsIssue(new CreateGoodsIssueRequestDto())).StatusOf().Should().Be(400);
            service.Verify(s => s.CreateGoodsIssueAsync(It.IsAny<CreateGoodsIssueRequestDto>(), It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task GoodsIssue_PostGoodsIssue_WithMalformedUserId_Returns400()
        {
            var service = new Mock<IGoodsIssueService>();
            var sut = new GoodsIssueController(service.Object).WithMalformedUserId();

            (await sut.PostGoodsIssue(Guid.NewGuid())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Nhánh phân trang còn thiếu của ProductController.</summary>
    public class ProductControllerBranchGapTests
    {
        private readonly Mock<IProductService> _service = new();
        private readonly ProductController _sut;

        public ProductControllerBranchGapTests()
            => _sut = new ProductController(_service.Object, new NoOpAuditLogService()).WithUser(Guid.NewGuid(), "Admin");

        [Fact]
        public async Task GetProducts_UsesDefaultPagingWhenNoArgumentsGiven()
        {
            _service.Setup(s => s.GetProductsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                    It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(new ProductPagedResultDto());

            (await _sut.GetProducts()).StatusOf().Should().Be(200);

            _service.Verify(s => s.GetProductsAsync(1, 12, null, null, null), Times.Once,
                "mặc định là trang 1, 12 sản phẩm, không lọc — hợp đồng với FE trang chủ");
        }
    }
}
