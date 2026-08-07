using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VietTien.API.Controllers;
using VietTien.API.Data;
using VietTien.API.DTOs.Customer;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Sales;
using VietTien.API.DTOs.SalesChange;
using VietTien.API.DTOs.UserProfile;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;
using VietTien.Tests.TestHelpers;
using Xunit;

namespace VietTien.Tests.Controllers
{
    /// <summary>Case code-driven phủ UserProfileController (302 dòng, trước đó 0%).</summary>
    public class UserProfileControllerTests
    {
        private readonly Mock<IUserProfileService> _profile = new();
        private readonly Mock<IAddressService> _address = new();
        private readonly UserProfileController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public UserProfileControllerTests()
            => _sut = new UserProfileController(_profile.Object, _address.Object).WithUser(_userId, "Customer");

        private static IFormFile FakeFile(long length = 10)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.SetupGet(f => f.FileName).Returns("avatar.png");
            return file.Object;
        }

        // ── profile ──────────────────────────────────────────────────────────

        [Fact]
        public async Task GetProfile_Success_ReturnsOk()
        {
            _profile.Setup(s => s.GetProfileAsync(_userId)).ReturnsAsync(new UserProfileDto());

            (await _sut.GetProfile()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetProfile_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetProfile()).StatusOf().Should().Be(401);
            _profile.Verify(s => s.GetProfileAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetProfile_WhenUserRowMissing_Returns404()
        {
            _profile.Setup(s => s.GetProfileAsync(_userId)).ThrowsAsync(new KeyNotFoundException("khong thay user"));

            (await _sut.GetProfile()).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateProfile_WhenModelStateInvalid_Returns400AndListsFieldErrors()
        {
            _sut.WithInvalidModelState("FullName", "khong duoc rong");

            var result = await _sut.UpdateProfile(new UpdateUserProfileDto());

            result.StatusOf().Should().Be(400);
            // Payload là anonymous object -> ToString() không in nội dung Dictionary, phải đọc qua reflection.
            var payload = (result as ObjectResult)!.Value!;
            var errors = (IDictionary<string, string[]>)payload.GetType().GetProperty("errors")!.GetValue(payload)!;
            errors.Should().ContainKey("FullName", "phải trả về tên field lỗi cho FE hiển thị");
            _profile.Verify(s => s.UpdateProfileAsync(It.IsAny<Guid>(), It.IsAny<UpdateUserProfileDto>()), Times.Never);
        }

        [Fact]
        public async Task UpdateProfile_Success_ReturnsOk()
        {
            _profile.Setup(s => s.UpdateProfileAsync(_userId, It.IsAny<UpdateUserProfileDto>()))
                .ReturnsAsync(new UserProfileDto());

            (await _sut.UpdateProfile(new UpdateUserProfileDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateProfile_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.UpdateProfile(new UpdateUserProfileDto())).StatusOf().Should().Be(401);
        }

        // ── avatar ───────────────────────────────────────────────────────────

        [Fact]
        public async Task UploadAvatar_Success_ReturnsOk()
        {
            _profile.Setup(s => s.UploadAvatarAsync(_userId, It.IsAny<IFormFile>()))
                .ReturnsAsync(new AvatarResponseDto());

            (await _sut.UploadAvatar(FakeFile())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UploadAvatar_WhenFileRejected_Returns400()
        {
            _profile.Setup(s => s.UploadAvatarAsync(It.IsAny<Guid>(), It.IsAny<IFormFile>()))
                .ThrowsAsync(new ArgumentException("Chỉ chấp nhận ảnh JPG/PNG"));

            (await _sut.UploadAvatar(FakeFile())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UploadAvatar_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.UploadAvatar(FakeFile())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteAvatar_Success_Returns204()
        {
            (await _sut.DeleteAvatar()).StatusOf().Should().Be(204);
            _profile.Verify(s => s.DeleteAvatarAsync(_userId), Times.Once);
        }

        [Fact]
        public async Task DeleteAvatar_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.DeleteAvatar()).StatusOf().Should().Be(401);
        }

        // ── password ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ChangePassword_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("NewPassword", "toi thieu 8 ky tu");

            (await _sut.ChangePassword(new ChangePasswordDto())).StatusOf().Should().Be(400);
            _profile.Verify(s => s.ChangePasswordAsync(It.IsAny<Guid>(), It.IsAny<ChangePasswordDto>()), Times.Never);
        }

        [Fact]
        public async Task ChangePassword_Success_ReturnsOk()
        {
            (await _sut.ChangePassword(new ChangePasswordDto())).StatusOf().Should().Be(200);
            _profile.Verify(s => s.ChangePasswordAsync(_userId, It.IsAny<ChangePasswordDto>()), Times.Once);
        }

        [Fact]
        public async Task ChangePassword_WhenCurrentPasswordWrong_Returns400()
        {
            _profile.Setup(s => s.ChangePasswordAsync(It.IsAny<Guid>(), It.IsAny<ChangePasswordDto>()))
                .ThrowsAsync(new InvalidOperationException("Mật khẩu hiện tại không đúng"));

            (await _sut.ChangePassword(new ChangePasswordDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task ChangePassword_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.ChangePassword(new ChangePasswordDto())).StatusOf().Should().Be(401);
        }

        // ── addresses ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetAddresses_Success_ReturnsOk()
        {
            _address.Setup(s => s.GetAddressesAsync(_userId)).ReturnsAsync(new List<AddressDto>());

            (await _sut.GetAddresses()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetAddresses_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetAddresses()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task CreateAddress_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("ReceiverName", "bat buoc");

            (await _sut.CreateAddress(new CreateAddressDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateAddress_Success_Returns201()
        {
            _address.Setup(s => s.CreateAddressAsync(_userId, It.IsAny<CreateAddressDto>()))
                .ReturnsAsync(new AddressDto());

            (await _sut.CreateAddress(new CreateAddressDto())).StatusOf().Should().Be(201);
        }

        [Fact]
        public async Task CreateAddress_WhenLimitReached_Returns400()
        {
            _address.Setup(s => s.CreateAddressAsync(It.IsAny<Guid>(), It.IsAny<CreateAddressDto>()))
                .ThrowsAsync(new InvalidOperationException("Vượt quá số địa chỉ cho phép"));

            (await _sut.CreateAddress(new CreateAddressDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task CreateAddress_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.CreateAddress(new CreateAddressDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task UpdateAddress_WhenModelStateInvalid_Returns400()
        {
            _sut.WithInvalidModelState("Street", "bat buoc");

            (await _sut.UpdateAddress(Guid.NewGuid(), new UpdateAddressDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateAddress_Success_ReturnsOk()
        {
            _address.Setup(s => s.UpdateAddressAsync(_userId, It.IsAny<Guid>(), It.IsAny<UpdateAddressDto>()))
                .ReturnsAsync(new AddressDto());

            (await _sut.UpdateAddress(Guid.NewGuid(), new UpdateAddressDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateAddress_WhenAddressMissing_Returns404()
        {
            _address.Setup(s => s.UpdateAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateAddressDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay dia chi"));

            (await _sut.UpdateAddress(Guid.NewGuid(), new UpdateAddressDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateAddress_WhenAddressOfAnotherUser_Returns401()
        {
            _address.Setup(s => s.UpdateAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateAddressDto>()))
                .ThrowsAsync(new UnauthorizedAccessException("khong phai dia chi cua ban"));

            (await _sut.UpdateAddress(Guid.NewGuid(), new UpdateAddressDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task DeleteAddress_Success_Returns204()
        {
            (await _sut.DeleteAddress(Guid.NewGuid())).StatusOf().Should().Be(204);
        }

        [Fact]
        public async Task DeleteAddress_WhenAddressInUse_Returns400()
        {
            _address.Setup(s => s.DeleteAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Địa chỉ đang được dùng"));

            (await _sut.DeleteAddress(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task DeleteAddress_WhenAddressMissing_Returns404()
        {
            _address.Setup(s => s.DeleteAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay"));

            (await _sut.DeleteAddress(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task DeleteAddress_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.DeleteAddress(Guid.NewGuid())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task SetDefaultAddress_Success_ReturnsOk()
        {
            (await _sut.SetDefaultAddress(Guid.NewGuid())).StatusOf().Should().Be(200);
            _address.Verify(s => s.SetDefaultAddressAsync(_userId, It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task SetDefaultAddress_WhenAddressMissing_Returns404()
        {
            _address.Setup(s => s.SetDefaultAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay"));

            (await _sut.SetDefaultAddress(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task SetDefaultAddress_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.SetDefaultAddress(Guid.NewGuid())).StatusOf().Should().Be(401);
        }
    }

    /// <summary>
    /// Case code-driven phủ CustomerProfileController (151 dòng, trước đó 0%).
    /// Controller dùng cả IUnitOfWork (mock được) lẫn ApplicationDbContext (EF InMemory).
    /// </summary>
    public class CustomerProfileControllerTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IUserRepository> _users = new();
        private readonly Mock<IAddressRepository> _addresses = new();
        private readonly ApplicationDbContext _db = TestDbFactory.Create();
        private readonly CustomerProfileController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public CustomerProfileControllerTests()
        {
            _uow.SetupGet(u => u.Users).Returns(_users.Object);
            _uow.SetupGet(u => u.Addresses).Returns(_addresses.Object);
            _sut = new CustomerProfileController(_uow.Object, _db).WithUser(_userId, "Customer");
        }

        [Fact]
        public async Task GetProfile_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetProfile()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetProfile_WhenNoProfileYet_ReturnsEmptyDtoNot404()
        {
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync((CustomerProfile?)null);

            var result = await _sut.GetProfile();

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value.Should().BeOfType<CustomerProfileDto>()
                .Which.TaxCode.Should().BeNull("khách chưa khai MST thì trả DTO rỗng chứ không phải lỗi");
        }

        [Fact]
        public async Task GetProfile_MapsAllCompanyFields()
        {
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(new CustomerProfile
            {
                UserId = _userId,
                TaxCode = "0312345678",
                CompanyName = "Cty Việt Tiến",
                CompanyAddress = "Hà Nội",
                InvoiceEmail = "hd@vt.vn",
                Representative = "Nguyễn A",
                CompanyPhone = "0900000000",
                AvailableCredit = 5_000_000m
            });

            var dto = ((await _sut.GetProfile()) as ObjectResult)!.Value.Should().BeOfType<CustomerProfileDto>().Subject;

            dto.TaxCode.Should().Be("0312345678");
            dto.CompanyName.Should().Be("Cty Việt Tiến");
            dto.AvailableCredit.Should().Be(5_000_000m);
        }

        [Fact]
        public async Task GetProfileStatus_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetProfileStatus()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetProfileStatus_WhenNoProfile_ReportsIncomplete()
        {
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync((CustomerProfile?)null);

            var body = ((await _sut.GetProfileStatus()) as ObjectResult)!.Value!.ToString();

            body.Should().Contain("False", "không có profile thì chưa thể mở khoá mua hàng");
        }

        [Fact]
        public async Task GetProfileStatus_WhenProfileHasAddress_ReportsCompleted()
        {
            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = _userId };
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(profile);
            _addresses.Setup(r => r.CountByCustomerProfileIdAsync(profile.Id)).ReturnsAsync(2);

            var body = ((await _sut.GetProfileStatus()) as ObjectResult)!.Value!.ToString();

            body.Should().Contain("True");
        }

        [Fact]
        public async Task GetProfileStatus_WhenProfileWithoutAddress_ReportsIncomplete()
        {
            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = _userId };
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(profile);
            _addresses.Setup(r => r.CountByCustomerProfileIdAsync(profile.Id)).ReturnsAsync(0);

            ((await _sut.GetProfileStatus()) as ObjectResult)!.Value!.ToString()
                .Should().Contain("False", "BR: phải có ít nhất 1 địa chỉ giao hàng mới mở khoá mua hàng");
        }

        [Fact]
        public async Task UpdateProfile_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.UpdateProfile(new CustomerProfileDto())).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task UpdateProfile_WhenNoProfileYet_CreatesOne()
        {
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync((CustomerProfile?)null);
            CustomerProfile? added = null;
            _users.Setup(r => r.AddCustomerProfileAsync(It.IsAny<CustomerProfile>()))
                .Callback<CustomerProfile>(p => added = p).Returns(Task.CompletedTask);

            (await _sut.UpdateProfile(new CustomerProfileDto { TaxCode = "0101", CompanyName = "ABC" }))
                .StatusOf().Should().Be(200);

            added.Should().NotBeNull();
            added!.UserId.Should().Be(_userId, "profile mới phải gắn đúng chủ sở hữu");
            added.TaxCode.Should().Be("0101");
            _uow.Verify(u => u.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task UpdateProfile_WhenProfileExists_UpdatesInPlaceWithoutInsert()
        {
            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = _userId, TaxCode = "cu" };
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(profile);

            (await _sut.UpdateProfile(new CustomerProfileDto { TaxCode = "moi" })).StatusOf().Should().Be(200);

            profile.TaxCode.Should().Be("moi");
            _users.Verify(r => r.AddCustomerProfileAsync(It.IsAny<CustomerProfile>()), Times.Never);
        }

        [Fact]
        public async Task GetProfileByPhone_WhenNotFound_Returns404()
        {
            _users.Setup(r => r.GetCustomerProfileByPhoneAsync("0900")).ReturnsAsync((CustomerProfile?)null);

            (await _sut.GetProfileByPhone("0900")).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetProfileByPhone_WhenFound_ReturnsProfileWithoutCredit()
        {
            _users.Setup(r => r.GetCustomerProfileByPhoneAsync("0900")).ReturnsAsync(new CustomerProfile
            {
                UserId = Guid.NewGuid(),
                CompanyName = "Cty X",
                AvailableCredit = 9_000_000m
            });

            var dto = ((await _sut.GetProfileByPhone("0900")) as ObjectResult)!.Value
                .Should().BeOfType<CustomerProfileDto>().Subject;

            dto.CompanyName.Should().Be("Cty X");
            dto.AvailableCredit.Should().Be(0,
                "endpoint tra cứu theo SĐT cho Sales POS cố tình không map hạn mức tín dụng");
        }

        [Fact]
        public async Task GetCreditHistory_WithoutUserClaim_Returns401()
        {
            _sut.WithAnonymousUser();

            (await _sut.GetCreditHistory()).StatusOf().Should().Be(401);
        }

        [Fact]
        public async Task GetCreditHistory_WhenNoProfile_ReturnsEmptyList()
        {
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync((CustomerProfile?)null);

            var result = await _sut.GetCreditHistory();

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value.Should().BeAssignableTo<List<CreditTransactionDto>>().Which.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCreditHistory_ReturnsOnlyOwnRowsNewestFirst()
        {
            var profile = new CustomerProfile { Id = Guid.NewGuid(), UserId = _userId };
            _users.Setup(r => r.GetCustomerProfileByUserIdAsync(_userId)).ReturnsAsync(profile);

            _db.CreditTransactions.AddRange(
                new CreditTransaction { CustomerProfileId = profile.Id, Amount = 100, Description = "cu", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new CreditTransaction { CustomerProfileId = profile.Id, Amount = 200, Description = "moi", CreatedAt = DateTime.UtcNow },
                new CreditTransaction { CustomerProfileId = Guid.NewGuid(), Amount = 999, Description = "cua nguoi khac", CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();

            var rows = (((await _sut.GetCreditHistory()) as ObjectResult)!.Value as List<CreditTransactionDto>)!;

            rows.Should().HaveCount(2, "không được lộ giao dịch tín dụng của khách khác");
            rows[0].Description.Should().Be("moi", "phải sắp xếp mới nhất trước");
        }
    }

    /// <summary>Case code-driven phủ SalesController (92 dòng, trước đó 0%).</summary>
    public class SalesControllerTests
    {
        private readonly Mock<ISalesAllocationService> _service = new();
        private readonly SalesController _sut;
        private readonly Guid _staffId = Guid.NewGuid();

        public SalesControllerTests() => _sut = new SalesController(_service.Object).WithUser(_staffId, "SalesStaff");

        [Fact]
        public async Task GetMyCustomers_PassesCallerIdAndQuery()
        {
            var query = new MyCustomerQueryDto();
            _service.Setup(s => s.GetMyCustomersAsync(_staffId, query))
                .ReturnsAsync(new PagedResultDto<MyCustomerListItemDto>());

            (await _sut.GetMyCustomers(query)).StatusOf().Should().Be(200);
            _service.Verify(s => s.GetMyCustomersAsync(_staffId, query), Times.Once,
                "chỉ được lấy khách của chính Sale đang đăng nhập");
        }

        [Fact]
        public async Task GetMyCustomerDetail_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetMyCustomerDetailAsync(_staffId, It.IsAny<Guid>()))
                .ReturnsAsync(new MyCustomerDetailDto());

            (await _sut.GetMyCustomerDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMyCustomerDetail_WhenCustomerOfAnotherSale_Returns404()
        {
            _service.Setup(s => s.GetMyCustomerDetailAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException("khong phai khach cua ban"));

            (await _sut.GetMyCustomerDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task UpdateCustomerNote_Success_ReturnsOk()
        {
            (await _sut.UpdateCustomerNote(Guid.NewGuid(), new UpdateCustomerNoteRequest { Note = "goi lai sau" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.UpdateCustomerNoteAsync(_staffId, It.IsAny<Guid>(), "goi lai sau"), Times.Once);
        }

        [Fact]
        public async Task UpdateCustomerNote_WhenServiceThrows_Returns404()
        {
            _service.Setup(s => s.UpdateCustomerNoteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay khach"));

            (await _sut.UpdateCustomerNote(Guid.NewGuid(), new UpdateCustomerNoteRequest()))
                .StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetRoundRobin_ReturnsOk()
        {
            _service.Setup(s => s.GetRoundRobinStateAsync()).ReturnsAsync(new RoundRobinStateDto());

            (await _sut.GetRoundRobin()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AssignUnassignedCustomers_Success_ReturnsOk()
        {
            _service.Setup(s => s.AssignUnassignedCustomersAsync(_staffId))
                .ReturnsAsync(new RoundRobinAssignResultDto());

            (await _sut.AssignUnassignedCustomers()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task AssignUnassignedCustomers_WhenNoActiveSale_Returns400()
        {
            _service.Setup(s => s.AssignUnassignedCustomersAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Khong co Sale nao dang hoat dong"));

            (await _sut.AssignUnassignedCustomers()).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task UpdateRoundRobin_Success_ReturnsOk()
        {
            _service.Setup(s => s.UpdateRoundRobinAsync(_staffId, It.IsAny<UpdateRoundRobinRequest>()))
                .ReturnsAsync(new RoundRobinStateDto());

            (await _sut.UpdateRoundRobin(new UpdateRoundRobinRequest())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task UpdateRoundRobin_WhenServiceThrows_Returns400()
        {
            _service.Setup(s => s.UpdateRoundRobinAsync(It.IsAny<Guid>(), It.IsAny<UpdateRoundRobinRequest>()))
                .ThrowsAsync(new InvalidOperationException("Danh sach rong"));

            (await _sut.UpdateRoundRobin(new UpdateRoundRobinRequest())).StatusOf().Should().Be(400);
        }
    }

    /// <summary>Case code-driven phủ SalesChangeRequestsController (168 dòng, trước đó 0%).</summary>
    public class SalesChangeRequestsControllerTests
    {
        private readonly Mock<ISalesChangeRequestService> _service = new();
        private readonly SalesChangeRequestsController _sut;
        private readonly Guid _userId = Guid.NewGuid();

        public SalesChangeRequestsControllerTests()
            => _sut = new SalesChangeRequestsController(_service.Object).WithUser(_userId, "Customer");

        // ── customer ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Create_Success_ReturnsOkWithNewId()
        {
            var newId = Guid.NewGuid();
            _service.Setup(s => s.CreateAsync(_userId, It.IsAny<CreateSalesChangeRequestDto>())).ReturnsAsync(newId);

            var result = await _sut.Create(new CreateSalesChangeRequestDto());

            result.StatusOf().Should().Be(200);
            (result as ObjectResult)!.Value!.ToString().Should().Contain(newId.ToString());
        }

        [Fact]
        public async Task Create_WhenCustomerHasNoAssignedSale_Returns404()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateSalesChangeRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("chua co Sale phu trach"));

            (await _sut.Create(new CreateSalesChangeRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Create_WhenAlreadyHasPendingRequest_Returns400()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateSalesChangeRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Da co yeu cau dang cho xu ly"));

            (await _sut.Create(new CreateSalesChangeRequestDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task GetMine_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetMineAsync(_userId)).ReturnsAsync(new List<SalesChangeRequestDetailDto>());

            (await _sut.GetMine()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMine_WhenServiceThrows_Returns404()
        {
            _service.Setup(s => s.GetMineAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetMine()).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetMyAssignedSale_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetMyAssignedSaleAsync(_userId)).ReturnsAsync(new MyAssignedSaleDto());

            (await _sut.GetMyAssignedSale()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetMyAssignedSale_WhenNoneAssigned_Returns404()
        {
            _service.Setup(s => s.GetMyAssignedSaleAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("chua co"));

            (await _sut.GetMyAssignedSale()).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetSalesOptions_ReturnsOk()
        {
            _service.Setup(s => s.GetSalesOptionsAsync()).ReturnsAsync(new List<SalesOptionDto>());

            (await _sut.GetSalesOptions()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task Cancel_Success_ReturnsOk()
        {
            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.CancelAsync(_userId, It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task Cancel_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Cancel_WhenRequestBelongsToAnotherCustomer_Returns403()
        {
            _service.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task Cancel_WhenAlreadyProcessed_Returns400()
        {
            _service.Setup(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Yeu cau da duoc xu ly"));

            (await _sut.Cancel(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitAdditionalInfo_Success_ReturnsOk()
        {
            (await _sut.SubmitAdditionalInfo(Guid.NewGuid(), new AdditionalInfoDto { Info = "them thong tin" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.SubmitAdditionalInfoAsync(_userId, It.IsAny<Guid>(), "them thong tin"), Times.Once);
        }

        [Fact]
        public async Task SubmitAdditionalInfo_WhenNotOwner_Returns403()
        {
            _service.Setup(s => s.SubmitAdditionalInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.SubmitAdditionalInfo(Guid.NewGuid(), new AdditionalInfoDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task SubmitAdditionalInfo_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.SubmitAdditionalInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Khong o trang thai cho bo sung"));

            (await _sut.SubmitAdditionalInfo(Guid.NewGuid(), new AdditionalInfoDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitAdditionalInfo_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.SubmitAdditionalInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.SubmitAdditionalInfo(Guid.NewGuid(), new AdditionalInfoDto())).StatusOf().Should().Be(404);
        }

        // ── sales staff ──────────────────────────────────────────────────────

        [Fact]
        public async Task GetAboutMe_ReturnsOk()
        {
            _service.Setup(s => s.GetAboutMeAsync(_userId)).ReturnsAsync(new List<SalesChangeRequestDetailDto>());

            (await _sut.GetAboutMe()).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SubmitExplanation_Success_ReturnsOk()
        {
            (await _sut.SubmitExplanation(Guid.NewGuid(), new SaleExplanationDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task SubmitExplanation_WhenServiceThrowsInvalidOperation_Returns400()
        {
            // Lưu ý: trường hợp "Manager chưa mở giải trình" thực tế ném UnauthorizedAccessException
            // nên ra 403, không phải 400 — xem SalesChangeRequestServiceBranchTests. Case này chỉ
            // khẳng định phép ánh xạ InvalidOperationException -> 400 của controller.
            _service.Setup(s => s.SubmitExplanationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SaleExplanationDto>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await _sut.SubmitExplanation(Guid.NewGuid(), new SaleExplanationDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task SubmitExplanation_WhenNotTheAccusedSale_Returns403()
        {
            _service.Setup(s => s.SubmitExplanationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SaleExplanationDto>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            (await _sut.SubmitExplanation(Guid.NewGuid(), new SaleExplanationDto())).StatusOf().Should().Be(403);
        }

        [Fact]
        public async Task SubmitExplanation_WhenRequestMissing_Returns404()
        {
            _service.Setup(s => s.SubmitExplanationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SaleExplanationDto>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.SubmitExplanation(Guid.NewGuid(), new SaleExplanationDto())).StatusOf().Should().Be(404);
        }

        // ── manager ──────────────────────────────────────────────────────────

        [Fact]
        public async Task GetPaged_ReturnsOk()
        {
            _service.Setup(s => s.GetPagedAsync(It.IsAny<SalesChangeRequestQueryDto>()))
                .ReturnsAsync(new SalesChangeRequestPagedResultDto());

            (await _sut.GetPaged(new SalesChangeRequestQueryDto())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetDetail_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetDetailAsync(It.IsAny<Guid>())).ReturnsAsync(new SalesChangeRequestDetailDto());

            (await _sut.GetDetail(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetDetail_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetDetailAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetDetail(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task GetReviewContext_Success_ReturnsOk()
        {
            _service.Setup(s => s.GetReviewContextAsync(It.IsAny<Guid>())).ReturnsAsync(new ReviewContextDto());

            (await _sut.GetReviewContext(Guid.NewGuid())).StatusOf().Should().Be(200);
        }

        [Fact]
        public async Task GetReviewContext_WhenMissing_Returns404()
        {
            _service.Setup(s => s.GetReviewContextAsync(It.IsAny<Guid>())).ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.GetReviewContext(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestExplanation_Success_ReturnsOk()
        {
            (await _sut.RequestExplanation(Guid.NewGuid())).StatusOf().Should().Be(200);
            _service.Verify(s => s.RequestExplanationAsync(_userId, It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task RequestExplanation_WhenMissing_Returns404()
        {
            _service.Setup(s => s.RequestExplanationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.RequestExplanation(Guid.NewGuid())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestExplanation_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.RequestExplanationAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ThrowsAsync(new InvalidOperationException("Da o trang thai khac"));

            (await _sut.RequestExplanation(Guid.NewGuid())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task RequestMoreInfo_Success_ReturnsOk()
        {
            (await _sut.RequestMoreInfo(Guid.NewGuid(), new ManagerNoteDto { Note = "bo sung hoa don" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.RequestMoreInfoAsync(_userId, It.IsAny<Guid>(), "bo sung hoa don"), Times.Once);
        }

        [Fact]
        public async Task RequestMoreInfo_WhenMissing_Returns404()
        {
            _service.Setup(s => s.RequestMoreInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.RequestMoreInfo(Guid.NewGuid(), new ManagerNoteDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task RequestMoreInfo_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.RequestMoreInfoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await _sut.RequestMoreInfo(Guid.NewGuid(), new ManagerNoteDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Reject_Success_ReturnsOk()
        {
            (await _sut.Reject(Guid.NewGuid(), new ManagerNoteDto { Note = "khong du can cu" }))
                .StatusOf().Should().Be(200);
            _service.Verify(s => s.RejectAsync(_userId, It.IsAny<Guid>(), "khong du can cu"), Times.Once);
        }

        [Fact]
        public async Task Reject_WhenMissing_Returns404()
        {
            _service.Setup(s => s.RejectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new KeyNotFoundException("x"));

            (await _sut.Reject(Guid.NewGuid(), new ManagerNoteDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Reject_WhenWrongState_Returns400()
        {
            _service.Setup(s => s.RejectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("sai trang thai"));

            (await _sut.Reject(Guid.NewGuid(), new ManagerNoteDto())).StatusOf().Should().Be(400);
        }

        [Fact]
        public async Task Approve_Success_ReturnsOk()
        {
            (await _sut.Approve(Guid.NewGuid(), new ApproveSalesChangeRequestDto())).StatusOf().Should().Be(200);
            _service.Verify(s => s.ApproveAsync(_userId, It.IsAny<Guid>(), It.IsAny<ApproveSalesChangeRequestDto>()), Times.Once);
        }

        [Fact]
        public async Task Approve_WhenNewSaleMissing_Returns404()
        {
            _service.Setup(s => s.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ApproveSalesChangeRequestDto>()))
                .ThrowsAsync(new KeyNotFoundException("khong thay Sale moi"));

            (await _sut.Approve(Guid.NewGuid(), new ApproveSalesChangeRequestDto())).StatusOf().Should().Be(404);
        }

        [Fact]
        public async Task Approve_WhenNewSaleSameAsCurrent_Returns400()
        {
            _service.Setup(s => s.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ApproveSalesChangeRequestDto>()))
                .ThrowsAsync(new InvalidOperationException("Sale moi trung Sale hien tai"));

            (await _sut.Approve(Guid.NewGuid(), new ApproveSalesChangeRequestDto())).StatusOf().Should().Be(400);
        }
    }
}
